
namespace Core.Security.Services;

[Register]
public sealed partial class GitDiffProvider : IGitDiffProvider
{
    [Inject] private readonly ILogger<GitDiffProvider> _logger;
    [Inject] private readonly IProcessService _processService;

    public async Task<IReadOnlyList<string>> GetStagedFileNamesAsync(string workingDirectory, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        var output = await ExecuteGitCommandAsync("diff --cached --name-only", workingDirectory, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(output))
            return [];

        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim())
            .Where(f => f.Length > 0)
            .ToList();
    }

    public async Task<string> GetStagedDiffAsync(string workingDirectory, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        return await ExecuteGitCommandAsync("diff --cached", workingDirectory, ct).ConfigureAwait(false);
    }

    private async Task<string> ExecuteGitCommandAsync(string arguments, string workingDirectory, CancellationToken ct)
    {
        try
        {
            var options = new ProcessOptions
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
                StandardErrorEncoding = System.Text.Encoding.UTF8
            };

            var result = await _processService.ExecuteAsync(options, ct).ConfigureAwait(false);

            if (!result.Success)
            {
                _logger.LogWarning("Git 命令执行失败: git {Args}, ExitCode={ExitCode}", arguments, result.ExitCode);
                return string.Empty;
            }

            return result.StandardOutput.Trim();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "执行 Git 命令异常: git {Args}", arguments);
            return string.Empty;
        }
    }
}
