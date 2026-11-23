
namespace Core.Security.Services;

/// <summary>
/// Git Diff 提供者 — 通过 git 命令获取暂存区文件名与差异内容
/// </summary>
[Register(typeof(IGitDiffProvider), ServiceLifetime.Singleton)]
public sealed partial class GitDiffProvider : ServiceEntity, IGitDiffProvider {

    /// <summary>
    /// 构造函数 — 注入日志记录器与 Git 命令执行器
    /// </summary>
    /// <param name="logger">日志记录器</param>
    /// <param name="gitRunner">Git 命令执行器</param>
    public GitDiffProvider(ILogger<GitDiffProvider> logger, IGitCommandRunner gitRunner) {
        _logger = logger;
        _gitRunner = gitRunner;
    }
    private readonly ILogger<GitDiffProvider> _logger;
    private readonly IGitCommandRunner _gitRunner;

    /// <inheritdoc />
    public async Task<IReadOnlyList<string>> GetStagedFileNamesAsync(string workingDirectory, CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        var output = await ExecuteGitCommandAsync("diff --cached --name-only", workingDirectory, ct).ConfigureAwait(false);

        if (string.IsNullOrWhiteSpace(output))
            return [];

        return output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(f => f.Trim())
            .Where(f => f.Length > 0)
            .ToList();
    }

    /// <inheritdoc />
    public async Task<string> GetStagedDiffAsync(string workingDirectory, CancellationToken ct = default) {
        ArgumentException.ThrowIfNullOrWhiteSpace(workingDirectory);
        return await ExecuteGitCommandAsync("diff --cached", workingDirectory, ct).ConfigureAwait(false);
    }

    private async Task<string> ExecuteGitCommandAsync(string arguments, string workingDirectory, CancellationToken ct) {
        var result = await _gitRunner.ExecuteAsync(arguments, workingDirectory, ct).ConfigureAwait(false);

        if (!result.Success) {
            _logger.LogWarning("Git 命令执行失败: git {Args}, ExitCode={ExitCode}", arguments, result.ExitCode);
            return string.Empty;
        }

        return result.Output.Trim();
    }
}