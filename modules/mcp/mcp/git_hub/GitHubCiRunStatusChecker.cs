namespace McpToolDispatch;

/// <summary>
/// GitHub CI 运行状态检查器 — 实现 <see cref="ICiRunStatusChecker"/>，查 GitHub Actions in_progress run。
/// <para>
/// 流程：解析 git remote(owner/repo) → 获取当前分支(如需) → GET /actions/runs?branch=&amp;status=in_progress
/// 非GitHub仓库或未配置 API 客户端时返回 IsRunning=false(不拦截)。
/// </para>
/// </summary>
[Register(typeof(ICiRunStatusChecker), ServiceLifetime.Singleton)]
public sealed class GitHubCiRunStatusChecker : ICiRunStatusChecker {
    private readonly IGitCommandRunner? _git;
    private readonly IGitHubApiClient? _apiClient;

    /// <summary>
    /// 构造 GitHub CI 运行状态检查器。
    /// </summary>
    /// <param name="git">git 命令执行器（可选，未注入时不拦截）</param>
    /// <param name="apiClient">GitHub API 客户端（可选，未注入时不拦截）</param>
    public GitHubCiRunStatusChecker(IGitCommandRunner? git = null, IGitHubApiClient? apiClient = null) {
        _git = git;
        _apiClient = apiClient;
    }

    /// <inheritdoc />
    public async ValueTask<CiRunStatus> CheckBranchCiStatusAsync(string workingDirectory, string? branch, CancellationToken ct) {
        if (_git is null || _apiClient is null)
            return new CiRunStatus(false, branch ?? "", 0, null);

        var resolvedBranch = await ResolveBranchAsync(workingDirectory, branch, ct).ConfigureAwait(false);
        if (resolvedBranch is null)
            return new CiRunStatus(false, branch ?? "", 0, null);

        var remoteInfo = await ResolveOwnerRepoAsync(workingDirectory, ct).ConfigureAwait(false);
        if (remoteInfo is null)
            return new CiRunStatus(false, resolvedBranch, 0, null);

        var (owner, repo) = remoteInfo.Value;
        return await QueryCiRunsAsync(owner, repo, resolvedBranch, ct).ConfigureAwait(false);
    }

    /// <summary>获取当前分支（branch 为空时用 git rev-parse）。</summary>
    private async Task<string?> ResolveBranchAsync(string workingDirectory, string? branch, CancellationToken ct) {
        if (!string.IsNullOrWhiteSpace(branch)) return branch.Trim();
        var result = await _git!.ExecuteAsync("rev-parse --abbrev-ref HEAD", workingDirectory, ct).ConfigureAwait(false);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output)) return null;
        var b = result.Output.Trim();
        return b is "HEAD" or "(no branch)" ? null : b;
    }

    /// <summary>解析 git remote origin → (owner, repo)。</summary>
    private async Task<(string owner, string repo)?> ResolveOwnerRepoAsync(string workingDirectory, CancellationToken ct) {
        var result = await _git!.ExecuteAsync("remote get-url origin", workingDirectory, ct).ConfigureAwait(false);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output)) return null;
        return GitHubToolHandlers.ParseGitHubRemoteUrl(result.Output.Trim());
    }

    /// <summary>查 GitHub Actions in_progress run。</summary>
    private async ValueTask<CiRunStatus> QueryCiRunsAsync(string owner, string repo, string branch, CancellationToken ct) {
        var query = new Dictionary<string, string> { ["branch"] = branch, ["status"] = "in_progress", ["per_page"] = "1" };
        var result = await _apiClient!.SendAsync(HttpMethod.Get, $"repos/{owner}/{repo}/actions/runs", query: query, ct: ct).ConfigureAwait(false);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Body))
            return new CiRunStatus(false, branch, 0, null);

        try {
            using var doc = JsonDocument.Parse(result.Body);
            var totalCount = doc.RootElement.TryGetProperty("total_count", out var tc) ? tc.GetInt32() : 0;
            if (totalCount <= 0)
                return new CiRunStatus(false, branch, 0, null);
            string? runUrl = null;
            if (doc.RootElement.TryGetProperty("workflow_runs", out var runs) && runs.GetArrayLength() > 0
                && runs[0].TryGetProperty("html_url", out var url))
                runUrl = url.GetString();
            return new CiRunStatus(true, branch, totalCount, runUrl);
        } catch {
            return new CiRunStatus(false, branch, 0, null);
        }
    }
}
