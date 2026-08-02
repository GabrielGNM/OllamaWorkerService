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
    private readonly string _ollamaModel = "hermes"; // Nome do modelo no Ollama

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
        _logger.LogInformation("Ollama Worker iniciado. Iniciando loop de conversação.");

        // Define a ferramenta de terminal
        var tools = new List<Tool>
        {
            new Tool
            {
                Type = "function",
                Function = new FunctionDefinition
                {
                    Name = "execute_terminal_command",
                    Description = "Executa um comando de terminal no Ubuntu (bash) e retorna a saída. Utilize para checar o status do sistema, por exemplo: /opt/rocm/bin/rocm-smi.",
                    Parameters = new
                    {
                        type = "object",
                        properties = new
                        {
                            command = new
                            {
                                type = "string",
                                description = "O comando bash a ser executado."
                            }
                        },
                        required = new[] { "command" }
                    }
                }
            }
        };

        // Histórico de mensagens inicial simulando um request de status da GPU
        var messages = new List<Message>
        {
            new Message
            {
                Role = "user",
                Content = "Verifique o status da GPU usando o rocm-smi e me informe a temperatura atual e o uso de VRAM."
            }
        };

        // Loop para lidar com chamadas contínuas se necessário
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Enviando requisição de Chat para o Ollama...");
                
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
                    _logger.LogWarning("Nenhuma resposta foi retornada pela API do Ollama. Tentando novamente em 5s.");
                    await Task.Delay(5000, stoppingToken);
                    continue;
                }

                // Adiciona a resposta do assistente no histórico
                messages.Add(responseMessage);

                // Se houver chamada para uma ferramenta
                if (responseMessage.ToolCalls != null && responseMessage.ToolCalls.Any())
                {
                    foreach (var toolCall in responseMessage.ToolCalls)
                    {
                        if (toolCall.Function.Name == "execute_terminal_command")
                        {
                            if (toolCall.Function.Arguments.TryGetValue("command", out var commandElement))
                            {
                                var command = ((JsonElement)commandElement).GetString();
                                _logger.LogInformation("LLM solicitou a execução do comando: {Command}", command);

                                // Cria CancellationToken com Timeout de 30 segundos
                                using var commandCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
                                commandCts.CancelAfter(TimeSpan.FromSeconds(30));

                                var commandOutput = await _terminalExecutor.ExecuteCommandAsync(command ?? string.Empty, commandCts.Token);
                                _logger.LogInformation("Saída do terminal recebida. Tamanho: {Length} caracteres.", commandOutput.Length);

                                // Retorna o resultado para o modelo como "tool" role
                                messages.Add(new Message
                                {
                                    Role = "tool",
                                    Content = commandOutput
                                });
                            }
                        }
                        else
                        {
                            _logger.LogWarning("LLM tentou invocar ferramenta desconhecida: {ToolName}", toolCall.Function.Name);
                        }
                    }
                    
                    // Continua o loop para reenviar as mensagens com a resposta da ferramenta,
                    // permitindo que o modelo gere a resposta final em linguagem natural.
                    continue;
                }

                // Quando não houver mais chamadas de ferramenta, é a resposta final
                _logger.LogInformation("===============================================");
                _logger.LogInformation("Resposta final do modelo:");
                _logger.LogInformation("{Content}", responseMessage.Content);
                _logger.LogInformation("===============================================");
                
                // Em um cenário real, poderíamos processar novos inputs do usuário.
                // Neste exemplo, iremos encerrar ou pausar a iteração contínua para evitar loop infinito
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Falha durante o ciclo de comunicação com o LLM.");
                await Task.Delay(5000, stoppingToken);
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
