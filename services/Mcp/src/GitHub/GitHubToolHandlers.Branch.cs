namespace McpToolDispatch;

/// <summary>
/// GitHub 分支保护工具 — 同步 CI check 名到分支保护规则 required_status_checks
/// <para>场景: CI workflow 拆分/重命名后 check 名变更, 分支保护规则需同步更新, 否则 auto-merge BLOCKED</para>
/// <para>ADR: 0073 — 直调 GitHub REST API, 不经 gh CLI/PowerShell</para>
/// </summary>
public partial class GitHubToolHandlers
{
    [McpTool(GitHubToolNameConstants.GhSyncBranchProtection, "同步分支保护规则: 从 PR 的 CI checks 提取 check 名, 更新分支保护的 required_status_checks(避免 CI 拆分后 auto-merge BLOCKED)", "github")]
    public async Task<ToolResult> GhSyncBranchProtectionAsync(
        [McpToolParameter("PR 编号或 URL(用于获取 CI check 名)", Required = true)] string pr_number,
        [McpToolParameter("分支名(默认 main)", Required = false)] string? branch = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
    {
        if (_apiClient is null) return ApiClientNotConfigured();
        var resolved = await ResolveOwnerRepoAsync(repo, working_dir, cancellationToken).ConfigureAwait(false);
        if (resolved is null) return RepoNotResolved();
        var (owner, repoName) = resolved.Value;
        var number = ParsePrNumber(pr_number);
        var branchName = string.IsNullOrWhiteSpace(branch) ? "main" : branch;

        var headSha = await GetPrHeadShaAsync(owner, repoName, number, cancellationToken).ConfigureAwait(false);
        if (headSha is null) return Fail("无法从 PR 响应中解析 head.sha");

        var checkNames = await GetCheckNamesAsync(owner, repoName, headSha, cancellationToken).ConfigureAwait(false);
        if (checkNames is null) return Fail("解析 check-runs 失败");
        if (checkNames.Count == 0) return Fail("PR 的 CI 没有任何 check-runs, 无法同步分支保护规则");

        var (strict, oldContexts) = await GetCurrentRequiredStatusChecksAsync(owner, repoName, branchName, cancellationToken).ConfigureAwait(false);
        if (oldContexts is null) return Fail($"分支 '{branchName}' 没有分支保护规则或 required_status_checks 未配置, 请先创建分支保护规则");

        var putBody = BuildRequiredStatusChecksBody(strict, checkNames);
        var putResult = await _apiClient.SendAsync(
            HttpMethod.Put,
            $"repos/{owner}/{repoName}/branches/{branchName}/protection/required_status_checks",
            body: putBody,
            ct: cancellationToken).ConfigureAwait(false);
        if (!putResult.Success) return Fail(putResult.Error);

        return Ok(BuildSyncSummary(owner, repoName, branchName, oldContexts, checkNames));
    }

    /// <summary>
    /// 获取 PR 的 head SHA
    /// </summary>
    private async Task<string?> GetPrHeadShaAsync(string owner, string repo, string number, CancellationToken ct)
    {
        if (_apiClient is null) return null;
        var prResult = await _apiClient.SendAsync(HttpMethod.Get, $"repos/{owner}/{repo}/pulls/{number}", ct: ct).ConfigureAwait(false);
        if (!prResult.Success)
        {
            _logger?.LogDebug("获取 PR 失败: {Error}", prResult.Error);
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(prResult.Body);
            return doc.RootElement.GetProperty("head").GetProperty("sha").GetString();
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "解析 PR head sha 失败");
            return null;
        }
    }

    /// <summary>
    /// 获取 commit 的所有 check-runs 名称(去重, 保序)
    /// </summary>
    private async Task<List<string>?> GetCheckNamesAsync(string owner, string repo, string sha, CancellationToken ct)
    {
        if (_apiClient is null) return null;
        var checksResult = await _apiClient.SendAsync(
            HttpMethod.Get,
            $"repos/{owner}/{repo}/commits/{sha}/check-runs",
            ct: ct).ConfigureAwait(false);
        if (!checksResult.Success)
        {
            _logger?.LogDebug("获取 check-runs 失败: {Error}", checksResult.Error);
            return null;
        }
        try
        {
            using var doc = JsonDocument.Parse(checksResult.Body);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var names = new List<string>();
            foreach (var run in doc.RootElement.GetProperty("check_runs").EnumerateArray())
            {
                var name = run.TryGetProperty("name", out var nameEl) ? nameEl.GetString() : null;
                if (!string.IsNullOrEmpty(name) && seen.Add(name))
                    names.Add(name);
            }
            return names;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "解析 check-runs 失败");
            return null;
        }
    }

    /// <summary>
    /// 获取当前分支保护的 required_status_checks — 返回 (strict, contexts), contexts=null 表示无保护规则
    /// </summary>
    private async Task<(bool strict, List<string>? contexts)> GetCurrentRequiredStatusChecksAsync(
        string owner, string repo, string branch, CancellationToken ct)
    {
        if (_apiClient is null) return (true, null);
        var result = await _apiClient.SendAsync(
            HttpMethod.Get,
            $"repos/{owner}/{repo}/branches/{branch}/protection/required_status_checks",
            ct: ct).ConfigureAwait(false);

        if (!result.Success)
        {
            if (result.StatusCode == 404) return (true, null);
            _logger?.LogDebug("获取 required_status_checks 失败: {Error}", result.Error);
            return (true, null);
        }

        try
        {
            using var doc = JsonDocument.Parse(result.Body);
            var strict = doc.RootElement.TryGetProperty("strict", out var strictEl) && strictEl.GetBoolean();
            var contexts = new List<string>();
            if (doc.RootElement.TryGetProperty("contexts", out var contextsEl))
            {
                foreach (var ctx in contextsEl.EnumerateArray())
                {
                    var ctxName = ctx.GetString();
                    if (!string.IsNullOrEmpty(ctxName))
                        contexts.Add(ctxName);
                }
            }
            return (strict, contexts);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "解析 required_status_checks 失败, 使用默认值");
            return (true, new List<string>());
        }
    }

    /// <summary>
    /// 构造 required_status_checks PUT body — AOT 友好(Utf8JsonWriter 流式写, 无 JsonNode.Add)
    /// </summary>
    private static string BuildRequiredStatusChecksBody(bool strict, IReadOnlyList<string> contexts)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("strict", strict);
            writer.WritePropertyName("contexts");
            writer.WriteStartArray();
            foreach (var ctx in contexts)
                writer.WriteStringValue(ctx);
            writer.WriteEndArray();
            writer.WriteEndObject();
            writer.Flush();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>
    /// 构建同步结果摘要
    /// </summary>
    private static string BuildSyncSummary(
        string owner, string repo, string branch,
        IReadOnlyList<string> oldContexts, IReadOnlyList<string> newContexts)
    {
        var sb = new StringBuilder(512);
        sb.AppendLine($"分支保护规则已同步: {owner}/{repo} 分支 '{branch}'");
        sb.AppendLine();
        sb.AppendLine($"旧 required_status_checks ({oldContexts.Count} 个):");
        foreach (var ctx in oldContexts) sb.AppendLine($"  - {ctx}");
        sb.AppendLine();
        sb.AppendLine($"新 required_status_checks ({newContexts.Count} 个):");
        foreach (var ctx in newContexts) sb.AppendLine($"  - {ctx}");

        var added = newContexts.Except(oldContexts, StringComparer.Ordinal).ToList();
        var removed = oldContexts.Except(newContexts, StringComparer.Ordinal).ToList();
        sb.AppendLine();
        sb.Append($"变更: +{added.Count} 新增, -{removed.Count} 移除");
        if (added.Count > 0)
        {
            sb.AppendLine();
            sb.Append("新增: ");
            sb.Append(string.Join(", ", added));
        }
        if (removed.Count > 0)
        {
            sb.AppendLine();
            sb.Append("移除: ");
            sb.Append(string.Join(", ", removed));
        }

        return sb.ToString();
    }
}
