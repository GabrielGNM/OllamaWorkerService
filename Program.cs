using Microsoft.AspNetCore.Mvc;
using OllamaWorkerService.Services;

var builder = WebApplication.CreateBuilder(args);

// Configura DI para o serviço de terminal
builder.Services.AddSingleton<ITerminalExecutorService, TerminalExecutorService>();

var app = builder.Build();

app.MapPost("/api/execute", async (
    [FromBody] ExecuteCommandRequest request,
    [FromServices] ITerminalExecutorService terminalExecutor,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Command))
    {
        return Results.BadRequest(new { error = "O comando não pode ser vazio." });
    }

    try
    {
        // Cria um timeout de segurança para a execução do comando (ex: 2 minutos)
        using var commandCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        commandCts.CancelAfter(TimeSpan.FromMinutes(2));

        var output = await terminalExecutor.ExecuteCommandAsync(request.Command, commandCts.Token);
        
        return Results.Ok(new { output });
    }
    catch (Exception ex)
    {
        return Results.Problem(detail: ex.Message, statusCode: 500);
    }
});

app.Run();

public class ExecuteCommandRequest
{
    public string Command { get; set; } = string.Empty;
}
