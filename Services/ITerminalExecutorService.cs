namespace OllamaWorkerService.Services;

public interface ITerminalExecutorService
{
    Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken);
}
