namespace McpToolDispatch;

/// <summary>
/// 分支保护审计结果 — 三类差异 + 原始数据
/// </summary>
/// <param name="Branch">审计的分支名</param>
/// <param name="CiJobNames">CI yml matrix 中的测试 job 名</param>
/// <param name="RequiredChecks">GitHub 分支保护 required_status_checks</param>
/// <param name="Matched">两边都有的 check 名</param>
/// <param name="MissingFromProtection">CI 有但保护缺(需添加)</param>
/// <param name="StaleInProtection">保护有但 CI 无(已删除)</param>
/// <param name="Note">附加说明(null=无)</param>
internal sealed record BranchProtectionAuditResult(
    string Branch,
    IReadOnlyList<string> CiJobNames,
    IReadOnlyList<string> RequiredChecks,
    IReadOnlyList<string> Matched,
    IReadOnlyList<string> MissingFromProtection,
    IReadOnlyList<string> StaleInProtection,
    string? Note = null) {

    /// <summary>
    /// 是否完全一致(无差异)
    /// </summary>
    internal bool IsConsistent => MissingFromProtection.Count == 0 && StaleInProtection.Count == 0;

    /// <summary>
    /// 构建可读报告文本 — 含三类差异 + AI 引导建议
    /// </summary>
    internal string BuildReport() {
        var sb = new StringBuilder(512);
        sb.AppendLine($"分支保护审计: 分支 '{Branch}'");
        sb.AppendLine();
        if (Note is not null) { sb.AppendLine(Note); sb.AppendLine(); }
        sb.AppendLine($"CI matrix 测试 job ({CiJobNames.Count} 个):");
        foreach (var name in CiJobNames) sb.AppendLine($"  - {name}");
        sb.AppendLine();
        sb.AppendLine($"required_status_checks ({RequiredChecks.Count} 个):");
        foreach (var ctx in RequiredChecks) sb.AppendLine($"  - {ctx}");
        sb.AppendLine();
        sb.AppendLine($"✅ 匹配 ({Matched.Count} 个): {string.Join(", ", Matched)}");
        sb.AppendLine($"⚠️ CI 有但保护缺 ({MissingFromProtection.Count} 个): {string.Join(", ", MissingFromProtection)}");
        sb.AppendLine($"❌ 保护有但 CI 无 ({StaleInProtection.Count} 个): {string.Join(", ", StaleInProtection)}");
        sb.AppendLine();
        if (MissingFromProtection.Count > 0) {
            sb.AppendLine("建议: 以下测试项目已加入 CI 但未锁定到分支保护, 运行 gh branch sync-protection 添加:");
            foreach (var name in MissingFromProtection) sb.AppendLine($"  + {name}");
        }
        if (StaleInProtection.Count > 0) {
            sb.AppendLine("警告: 以下 required_status_checks 在 CI 中已不存在, 可能测试项目已删除, 运行 gh branch sync-protection 清理:");
            foreach (var name in StaleInProtection) sb.AppendLine($"  - {name}");
        }
        if (IsConsistent)
            sb.AppendLine("✅ CI matrix 与分支保护完全一致, 无需操作");
        return sb.ToString();
    }
}

/// <summary>
/// 分支保护审计服务 — 对比 CI yml matrix 测试 job 名与 GitHub required_status_checks
/// <para>单一职责:读取 yml → 解析 → 查询 GitHub API → map 对比 → 返回差异</para>
/// <para>DI 服务:注入 IGitHubApiClient + IFileSystem, 可被 gh branch audit-protection 和 gh pr create 复用</para>
/// <para>map 对比:HashSet O(1) 查找, 求差集报告三类</para>
/// </summary>
internal sealed class BranchProtectionAuditor {
    private readonly IGitHubApiClient _apiClient;
    private readonly IFileSystem _fs;
    private readonly ILogger<BranchProtectionAuditor>? _logger;

    internal BranchProtectionAuditor(IGitHubApiClient apiClient, IFileSystem fs, ILogger<BranchProtectionAuditor>? logger = null) {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _logger = logger;
    }

    /// <summary>
    /// 审计分支保护一致性 — 读取 CI yml → 解析 job 名 → 查询 GitHub required_status_checks → map 对比
    /// </summary>
    /// <param name="owner">仓库 owner</param>
    /// <param name="repo">仓库名</param>
    /// <param name="branch">分支名(如 main)</param>
    /// <param name="ymlPath">CI yml 文件绝对路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>审计结果(三类差异), 失败时 Note 含错误说明</returns>
    internal async Task<BranchProtectionAuditResult> AuditAsync(
        string owner, string repo, string branch, string ymlPath, CancellationToken cancellationToken = default) {
        if (!_fs.FileExists(ymlPath))
            return EmptyResult(branch, $"CI yml 文件不存在: {ymlPath}");

        var ymlContent = await _fs.ReadAllTextAsync(ymlPath, cancellationToken).ConfigureAwait(false);
        var ciJobNames = CiMatrixParser.ExtractMatrixJobNames(ymlContent);
        if (ciJobNames.Count == 0)
            return EmptyResult(branch, $"未能从 {ymlPath} 中解析出 matrix job 名");

        var jobId = CiMatrixParser.ExtractJobId(ymlContent) ?? "unit-tests";
        var requiredChecks = await GetRequiredStatusChecksAsync(owner, repo, branch, cancellationToken).ConfigureAwait(false);
        if (requiredChecks is null)
            return BuildResult(branch, ciJobNames, [], "分支无保护规则或 required_status_checks 未配置, 建议先创建分支保护规则");

        // 从 required_status_checks 中提取对应 job_id 的 matrix name, 其他 workflow 的 check 不在审计范围
        var requiredMatrixNames = requiredChecks
            .Select(c => CiMatrixParser.ExtractMatrixNameFromCheck(c, jobId))
            .Where(n => n is not null)
            .Cast<string>()
            .ToList();
        return BuildResult(branch, ciJobNames, requiredMatrixNames);
    }

    private async Task<List<string>?> GetRequiredStatusChecksAsync(string owner, string repo, string branch, CancellationToken ct) {
        var result = await _apiClient.SendAsync(
            HttpMethod.Get,
            $"repos/{owner}/{repo}/branches/{branch}/protection/required_status_checks",
            ct: ct).ConfigureAwait(false);
        if (!result.Success) {
            if (result.StatusCode == 404) return null;
            _logger?.LogDebug("获取 required_status_checks 失败: {Error}", result.Error);
            return null;
        }
        try {
            using var doc = JsonDocument.Parse(result.Body);
            var contexts = new List<string>();
            if (doc.RootElement.TryGetProperty("contexts", out var contextsEl)) {
                foreach (var ctx in contextsEl.EnumerateArray()) {
                    var ctxName = ctx.GetString();
                    if (!string.IsNullOrEmpty(ctxName)) contexts.Add(ctxName);
                }
            }
            return contexts;
        } catch (Exception ex) {
            _logger?.LogDebug(ex, "解析 required_status_checks 失败");
            return null;
        }
    }

    private static BranchProtectionAuditResult BuildResult(
        string branch, IReadOnlyList<string> ciJobNames, IReadOnlyList<string> requiredChecks, string? note = null) {
        var ciSet = new HashSet<string>(ciJobNames, StringComparer.Ordinal);
        var reqSet = new HashSet<string>(requiredChecks, StringComparer.Ordinal);
        return new BranchProtectionAuditResult(
            branch, ciJobNames, requiredChecks,
            Matched: ciSet.Intersect(reqSet).OrderBy(x => x, StringComparer.Ordinal).ToList(),
            MissingFromProtection: ciSet.Except(reqSet).OrderBy(x => x, StringComparer.Ordinal).ToList(),
            StaleInProtection: reqSet.Except(ciSet).OrderBy(x => x, StringComparer.Ordinal).ToList(),
            Note: note);
    }

    private static BranchProtectionAuditResult EmptyResult(string branch, string note)
        => new(branch, [], [], [], [], [], note);
}
