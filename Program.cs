using OllamaWorkerService;
using OllamaWorkerService.Services;

var builder = Host.CreateApplicationBuilder(args);

// Configura DI para o serviço de terminal
builder.Services.AddSingleton<ITerminalExecutorService, TerminalExecutorService>();

// Configura o HttpClientFactory para a API do Ollama local
builder.Services.AddHttpClient("OllamaClient", client =>
{
    // Porta e host padrão do Ollama
    client.BaseAddress = new Uri("http://localhost:11434");
    
    // Modelos LLM rodando localmente podem ser lentos e demandar timeout elevado
    client.Timeout = TimeSpan.FromMinutes(5);
});

// Registra o serviço em background
builder.Services.AddHostedService<Worker>();

var host = builder.Build();
host.Run();
