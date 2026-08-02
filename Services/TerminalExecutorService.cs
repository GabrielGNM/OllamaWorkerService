using System.Diagnostics;
using System.Text;

namespace OllamaWorkerService.Services;

public class TerminalExecutorService : ITerminalExecutorService
{
    private readonly ILogger<TerminalExecutorService> _logger;

    public TerminalExecutorService(ILogger<TerminalExecutorService> logger)
    {
        _logger = logger;
    }

    public async Task<string> ExecuteCommandAsync(string command, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Executando comando no terminal: {Command}", command);
        
        try
        {
            // Se o diretório /host existir, assumimos que estamos em um container 
            // mapeado com '-v /:/host' e queremos rodar o comando no sistema host real.
            bool useHostRoot = System.IO.Directory.Exists("/host");
            
            var processStartInfo = new ProcessStartInfo
            {
                FileName = useHostRoot ? "chroot" : "/bin/bash",
                Arguments = useHostRoot 
                    ? $"/host /bin/bash -c \"{command.Replace("\"", "\\\"")}\""
                    : $"-c \"{command.Replace("\"", "\\\"")}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = new Process { StartInfo = processStartInfo };
            
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            process.OutputDataReceived += (sender, args) => 
            {
                if (args.Data != null) outputBuilder.AppendLine(args.Data);
            };
            
            process.ErrorDataReceived += (sender, args) => 
            {
                if (args.Data != null) errorBuilder.AppendLine(args.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Aguarda a execução finalizando no timeout configurado no token
            await process.WaitForExitAsync(cancellationToken);

            var output = outputBuilder.ToString().Trim();
            var error = errorBuilder.ToString().Trim();

            if (process.ExitCode != 0)
            {
                _logger.LogWarning("Comando finalizou com código {ExitCode}. Erro: {Error}", process.ExitCode, error);
                return $"Error (ExitCode {process.ExitCode}):\n{error}";
            }

            _logger.LogInformation("Comando executado com sucesso.");
            return string.IsNullOrEmpty(output) ? "Command executed successfully with no output." : output;
        }
        catch (OperationCanceledException)
        {
            _logger.LogError("Execução do comando cancelada por timeout: {Command}", command);
            return "Error: Command execution timed out.";
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erro ao executar comando: {Command}", command);
            return $"Error: {ex.Message}";
        }
    }
}
