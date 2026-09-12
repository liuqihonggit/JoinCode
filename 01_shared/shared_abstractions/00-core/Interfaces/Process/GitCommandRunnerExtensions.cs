namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// IGitCommandRunner 扩展方法 — 提取共享的 git 操作,消除 AutoRebaseService 与 AgentWorktreeService 重复
/// </summary>
public static class GitCommandRunnerExtensions
{
    /// <summary>
    /// 检查工作区是否有未提交修改 — git status --porcelain 输出非空则脏
    /// </summary>
    public static async Task<bool> HasUncommittedChangesAsync(
        this IGitCommandRunner runner, string workDir, CancellationToken ct = default)
    {
        var result = await runner.ExecuteAsync($"{GitSubCommand.Status.ToValue()} --porcelain", workDir, ct).ConfigureAwait(false);
        return result.Success && !string.IsNullOrWhiteSpace(result.Output);
    }

    /// <summary>
    /// 获取本地领先上游的提交数 — git rev-list --count HEAD..{upstream}
    /// </summary>
    /// <returns>上游新提交数；解析失败返回 -1</returns>
    public static async Task<int> GetUpstreamCommitCountAsync(
        this IGitCommandRunner runner, string upstream, string workDir, CancellationToken ct = default)
    {
        var result = await runner.ExecuteAsync($"{GitSubCommand.RevList.ToValue()} --count HEAD..{upstream}", workDir, ct).ConfigureAwait(false);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
            return -1;
        return int.TryParse(result.Output.Trim(), out var count) ? count : -1;
    }

    /// <summary>
    /// 获取 rebase/merge 冲突文件列表 — git diff --name-only --diff-filter=U
    /// </summary>
    public static async Task<IReadOnlyList<string>> GetConflictFilesAsync(
        this IGitCommandRunner runner, string workDir, CancellationToken ct = default)
    {
        var result = await runner.ExecuteAsync($"{GitSubCommand.Diff.ToValue()} --name-only --diff-filter=U", workDir, ct).ConfigureAwait(false);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
            return [];
        return result.Output
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
    }
}
