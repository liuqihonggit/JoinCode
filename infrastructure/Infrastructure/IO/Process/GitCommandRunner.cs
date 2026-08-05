namespace IO.ProcessService;

/// <summary>
/// Git 命令统一执行器 — 委托给 IProcessService，消除各处重复代码
/// </summary>
[Register]
public sealed partial class GitCommandRunner : IGitCommandRunner
{
    [Inject] private readonly IProcessService _processService;
    [Inject] private readonly ILogger<GitCommandRunner>? _logger;

    public async Task<GitCommandResult> ExecuteAsync(
        string arguments,
        string? workingDirectory = null,
        CancellationToken ct = default)
    {
        try
        {
            var options = new ProcessOptions
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8,
                EnvironmentVariables = new Dictionary<string, string>
                {
                    ["GIT_TERMINAL_PROMPT"] = "0",
                    ["GIT_ASKPASS"] = ""
                }
            };

            var result = await _processService.ExecuteAsync(options, ct).ConfigureAwait(false);

            return new GitCommandResult
            {
                Success = result.Success,
                Output = result.StandardOutput,
                Error = result.StandardError,
                ExitCode = result.ExitCode
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "执行 Git 命令失败: git {Arguments}", arguments);
            return new GitCommandResult
            {
                Success = false,
                Error = ex.Message,
                ExitCode = -1
            };
        }
    }
}
