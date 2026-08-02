using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using OllamaWorkerService.Models;
using OllamaWorkerService.Services;

namespace OllamaWorkerService;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ITerminalExecutorService _terminalExecutor;
    private readonly string _ollamaModel = "hermes";

    public Worker(
        ILogger<Worker> logger, 
        IHttpClientFactory httpClientFactory, 
        ITerminalExecutorService terminalExecutor)
    {
        _logger = logger;
        _httpClientFactory = httpClientFactory;
        _terminalExecutor = terminalExecutor;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Define a ferramenta de terminal GENÉRICA
        var tools = new List<Tool>
        {
            new Tool
            {
                Type = "function",
                Function = new FunctionDefinition
                {
                    Name = "execute_terminal_command",
                    Description = "Executa um comando de terminal (bash) e retorna a saída. Você tem permissão para executar qualquer comando no sistema Linux do usuário para buscar informações, modificar arquivos, instalar pacotes ou administrar o sistema.",
                    Parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            command = new
                            {
                                type = "string",
                                description = "O comando bash completo a ser executado."
                            }
                        },
                        required = new[] { "command" }
                    }
                }
            }
        };

        var messages = new List<Message>
        {
            new Message
            {
                Role = "system",
                Content = "Você é um assistente de IA focado em administração de sistemas Linux. Você possui uma ferramenta (execute_terminal_command) que permite rodar qualquer comando bash no sistema hospedeiro. Sempre que o usuário pedir algo, pense no comando necessário, execute a ferramenta e, com a saída gerada, responda de forma útil e objetiva."
            }
        };

        Console.WriteLine("==================================================");
        Console.WriteLine("Ollama Linux Terminal Assistant Iniciado!");
        Console.WriteLine("==================================================");

        while (!stoppingToken.IsCancellationRequested)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("\n[Usuário]: ");
            Console.ResetColor();

            var userInput = await Task.Run(() => Console.ReadLine(), stoppingToken);
            
            // Se userInput for null, significa que a stream de entrada padrão (stdin) foi fechada.
            // Isso acontece ao rodar no Docker sem a flag -i (interativo). 
            // Para não gerar loop infinito, pausamos a execução do Worker até ele ser encerrado.
            if (userInput == null)
            {
                _logger.LogWarning("Standard Input (stdin) foi fechado. Rodando em modo daemon sem TTY.");
                await Task.Delay(Timeout.Infinite, stoppingToken);
                break;
            }

            if (string.IsNullOrWhiteSpace(userInput)) continue;
            if (userInput.Equals("sair", StringComparison.OrdinalIgnoreCase) || userInput.Equals("exit", StringComparison.OrdinalIgnoreCase))
            {
                break;
            }

            messages.Add(new Message { Role = "user", Content = userInput });

            bool isAssistantDone = false;
            
            // Loop interno para permitir múltiplas chamadas de ferramenta antes da resposta final
            while (!isAssistantDone && !stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var request = new ChatRequest
                    {
                        Model = _ollamaModel,
                        Messages = messages,
                        Tools = tools,
                        Stream = false
                    };

                    var responseMessage = await SendChatRequestAsync(request, stoppingToken);
                    
                    if (responseMessage == null)
                    {
                        _logger.LogWarning("Nenhuma resposta foi retornada pela API. Tentando novamente.");
                        await Task.Delay(2000, stoppingToken);
                        continue;
                    }

                    messages.Add(responseMessage);

                    if (responseMessage.ToolCalls != null && responseMessage.ToolCalls.Any())
                    {
                        foreach (var toolCall in responseMessage.ToolCalls)
                        {
                            if (toolCall.Function.Name == "execute_terminal_command")
                            {
                                if (toolCall.Function.Arguments.TryGetValue("command", out var commandElement))
                                {
                                    var command = ((JsonElement)commandElement).GetString();
                                    
                                    Console.ForegroundColor = ConsoleColor.Yellow;
                                    Console.WriteLine($"\n[LLM rodando comando]: {command}");
                                    Console.ResetColor();

                                    using var commandCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                                    commandCts.CancelAfter(TimeSpan.FromMinutes(2)); // Maior timeout genérico

                                    var commandOutput = await _terminalExecutor.ExecuteCommandAsync(command ?? string.Empty, commandCts.Token);

                                    messages.Add(new Message
                                    {
                                        Role = "tool",
                                        Content = commandOutput
                                    });
                                }
                            }
                        }
                    }
                    else
                    {
                        // O LLM respondeu com texto e finalizou o raciocínio
                        Console.ForegroundColor = ConsoleColor.Cyan;
                        Console.WriteLine($"\n[Assistente]:\n{responseMessage.Content}");
                        Console.ResetColor();
                        isAssistantDone = true; // Quebra o loop interno para pedir novo input ao usuário
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Erro de comunicação com Ollama.");
                    break;
                }
            }
        }
    }

    private async Task<Message?> SendChatRequestAsync(ChatRequest request, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("OllamaClient");
        var jsonOptions = new JsonSerializerOptions 
        { 
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var jsonContent = new StringContent(JsonSerializer.Serialize(request, jsonOptions), Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/api/chat", jsonContent, cancellationToken);
        response.EnsureSuccessStatusCode();

        var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
        var chatResponse = JsonSerializer.Deserialize<ChatResponse>(responseJson, jsonOptions);

        return chatResponse?.Message;
    }
}
