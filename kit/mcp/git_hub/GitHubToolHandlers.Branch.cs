namespace McpToolDispatch;

/// <summary>
/// GitHub 分支保护工具 — 同步 CI check 名到分支保护规则 required_status_checks
/// <para>场景: CI workflow 拆分/重命名后 check 名变更, 分支保护规则需同步更新, 否则 auto-merge BLOCKED</para>
/// <para>ADR: 0073 — 直调 GitHub REST API, 不经 gh CLI/PowerShell</para>
/// </summary>
public partial class GitHubToolHandlers {
    /// <summary>
    /// 同步分支保护规则 — 从 CI yml matrix 读取测试 job 名, 对比 GitHub required_status_checks, 用完整 protection 端点 PUT 差异
    /// <para>场景: 新增/删除测试项目后, CI matrix 与分支保护 required_status_checks 不一致, 导致 auto-merge BLOCKED 或跳过必要检查</para>
    /// <para>流程: 读 yml → 解析 matrix → 审计差异 → GET 完整 protection → 构造新 checks → PUT 完整 protection 端点</para>
    /// <para>注意: required_status_checks 子端点不支持单独 PUT(返回 404), 必须用完整 protection 端点(ADR 0132)</para>
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhBranchSyncProtection, "同步分支保护: 从所有 CI yml 读取 job 名, 对比 required_status_checks, PUT 完整 protection 端点同步差异", "github")]
    public async Task<ToolResult> GhBranchSyncProtectionAsync(
        [McpToolParameter("分支名(默认 main)", Required = false)] string? branch = null,
        [McpToolParameter("CI yml 路径(可选,默认自动从 ci.yml 解析所有 workflow)", Required = false)] string? yml_path = null,
        [McpToolParameter("试跑模式(只显示差异不实际修改)", Required = false)] bool? dry_run = null,
        [McpToolOptions] GitHubCommonOptions? common = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(common?.Repo, common?.WorkingDir, cancellationToken, async (client, owner, repoName) => {
            var branchName = string.IsNullOrWhiteSpace(branch) ? "main" : branch;
            var workingDir = common?.WorkingDir is { } wd && !string.IsNullOrWhiteSpace(wd) ? wd : Environment.CurrentDirectory;
            var isDryRun = dry_run == true;

            var allCiCheckNames = await ExtractAllCiCheckNamesAsync(workingDir, yml_path, cancellationToken).ConfigureAwait(false);
            if (allCiCheckNames.Count == 0)
                return Fail("未能从 CI yml 文件中解析出任何 check 名");

            var protectionResult = await client.SendAsync(
                HttpMethod.Get, $"repos/{owner}/{repoName}/branches/{branchName}/protection",
                ct: cancellationToken).ConfigureAwait(false);
            if (!protectionResult.Success)
                return Fail($"获取分支保护规则失败: {protectionResult.Error}");

            var existingContexts = ExtractExistingContexts(protectionResult.Body);
            var (added, removed, newContexts) = ComputeSyncDiff(existingContexts, allCiCheckNames);
            if (added.Count == 0 && removed.Count == 0)
                return Ok($"✅ CI check 名与分支保护完全一致, 无需同步\n\n当前 required_status_checks ({newContexts.Count} 个):\n{string.Join('\n', newContexts.Select(c => $"  - {c}"))}");

            var putBody = BuildFullProtectionPutBody(protectionResult.Body, newContexts);
            if (isDryRun)
                return Ok(BuildDryRunSummary(owner, repoName, branchName, added, removed, newContexts));

            var putResult = await client.SendAsync(
                HttpMethod.Put, $"repos/{owner}/{repoName}/branches/{branchName}/protection",
                body: putBody, ct: cancellationToken).ConfigureAwait(false);
            if (!putResult.Success)
                return Fail($"PUT 分支保护规则失败: {putResult.Error}");

            return Ok(BuildSyncResultSummary(owner, repoName, branchName, added, removed, newContexts));
        }).ConfigureAwait(false);

    /// <summary>
    /// 审计分支保护规则 — 对比 CI yml matrix 的测试 job 名与 GitHub 分支保护 required_status_checks, 报告差异(不修改)
    /// <para>场景: 新增/删除测试项目后检查是否需要同步分支保护, 避免 auto-merge BLOCKED 或跳过必要检查</para>
    /// <para>对比: 用 HashSet map O(1) 查找, 求差集报告三类: ✅匹配 / ⚠️CI有但保护缺(需添加) / ❌保护有但CI无(已删除)</para>
    /// </summary>
    [McpTool(GitHubToolNameEnumConstants.GhBranchAuditProtection, "审计分支保护: 对比 CI yml matrix 测试 job 与 required_status_checks, 报告差异(不修改)", "github", ConcurrencySafe = true)]
    public async Task<ToolResult> GhBranchAuditProtectionAsync(
        [McpToolParameter("分支名(默认 main)", Required = false)] string? branch = null,
        [McpToolParameter("CI yml 路径(默认 .github/workflows/ci-unit-tests.yml)", Required = false)] string? yml_path = null,
        [McpToolOptions] GitHubCommonOptions? common = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(common?.Repo, common?.WorkingDir, cancellationToken, async (client, owner, repoName) => {
            var branchName = string.IsNullOrWhiteSpace(branch) ? "main" : branch;
            var ymlPath = string.IsNullOrWhiteSpace(yml_path) ? ".github/workflows/ci-unit-tests.yml" : yml_path;
            var workingDir = common?.WorkingDir is { } wd && !string.IsNullOrWhiteSpace(wd) ? wd : Environment.CurrentDirectory;
            var fullPath = Path.Combine(workingDir, ymlPath);
            var auditor = new BranchProtectionAuditor(client, _fs);
            var result = await auditor.AuditAsync(owner, repoName, branchName, fullPath, cancellationToken).ConfigureAwait(false);
            return Ok(result.BuildReport());
        }).ConfigureAwait(false);

    /// <summary>
    /// 从所有 CI workflow yml 提取全部 check 名 — 读 ci.yml 获取 workflow 映射, 再读每个 workflow 提取 check 名
    /// </summary>
    internal async Task<List<string>> ExtractAllCiCheckNamesAsync(
        string workingDir, string? ymlPath, CancellationToken ct) {
        var allCheckNames = new List<string>();
        if (!string.IsNullOrWhiteSpace(ymlPath)) {
            var directPath = Path.Combine(workingDir, ymlPath);
            if (!_fs.FileExists(directPath)) return allCheckNames;
            var ymlContent = await _fs.ReadAllTextAsync(directPath, ct).ConfigureAwait(false);
            allCheckNames.AddRange(CiMatrixParser.ExtractAllCheckNames(ymlContent, "unit-tests"));
            return allCheckNames;
        }
        var ciYmlPath = Path.Combine(workingDir, ".github/workflows/ci.yml");
        if (!_fs.FileExists(ciYmlPath)) return allCheckNames;
        var ciYmlContent = await _fs.ReadAllTextAsync(ciYmlPath, ct).ConfigureAwait(false);
        var workflowUses = CiMatrixParser.ExtractWorkflowUses(ciYmlContent);
        foreach (var (outerJobId, workflowPath) in workflowUses) {
            var fullPath = Path.Combine(workingDir, workflowPath);
            if (!_fs.FileExists(fullPath)) continue;
            var ymlContent = await _fs.ReadAllTextAsync(fullPath, ct).ConfigureAwait(false);
            allCheckNames.AddRange(CiMatrixParser.ExtractAllCheckNames(ymlContent, outerJobId));
        }
        return allCheckNames;
    }

    /// <summary>
    /// 从 protection JSON 提取现有 required_status_checks.contexts
    /// </summary>
    internal static List<string> ExtractExistingContexts(string protectionJson) {
        var resp = JsonSerializer.Deserialize(protectionJson, GitHubApiJsonContext.Safe.BranchProtectionContextsResponse);
        return resp?.RequiredStatusChecks?.Contexts ?? new List<string>();
    }

    /// <summary>
    /// 计算同步差异 — added(CI 有但保护缺), removed(保护有但 CI 无), newContexts(同步后的完整列表)
    /// </summary>
    internal static (List<string> Added, List<string> Removed, List<string> NewContexts) ComputeSyncDiff(
        IReadOnlyList<string> existingContexts, IReadOnlyList<string> ciCheckNames) {
        var ciSet = new HashSet<string>(ciCheckNames, StringComparer.Ordinal);
        var existingSet = new HashSet<string>(existingContexts, StringComparer.Ordinal);
        var added = ciSet.Except(existingSet).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var removed = existingSet.Except(ciSet).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var newContexts = ciCheckNames.OrderBy(x => x, StringComparer.Ordinal).ToList();
        return (added, removed, newContexts);
    }

    /// <summary>
    /// 构造完整 protection PUT body — 保留原有保护规则字段, 仅替换 required_status_checks.checks
    /// <para>AOT 友好: Utf8JsonWriter 流式写, 无 JsonNode 反射</para>
    /// <para>注意: required_status_checks 子端点不支持单独 PUT(404), 必须用完整 protection 端点(ADR 0132)</para>
    /// </summary>
    internal static string BuildFullProtectionPutBody(string protectionJson, IReadOnlyList<string> newContexts) {
        using var doc = JsonDocument.Parse(protectionJson);
        var root = doc.RootElement;

        var bufferWriter = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(bufferWriter, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping })) {
            writer.WriteStartObject();

            WriteRequiredStatusChecks(writer, root, newContexts);
            WriteEnforceAdmins(writer, root);
            WriteSimpleBool(writer, root, "required_linear_history");
            WriteSimpleBool(writer, root, "allow_force_pushes");
            WriteSimpleBool(writer, root, "allow_deletions");
            WriteSimpleBool(writer, root, "block_creations");
            WriteSimpleBool(writer, root, "required_conversation_resolution");
            WriteSimpleBool(writer, root, "lock_branch");
            WriteSimpleBool(writer, root, "allow_fork_syncing");

            writer.WriteNull("required_pull_request_reviews");
            writer.WriteNull("restrictions");

            writer.WriteEndObject();
            writer.Flush();
        }
        return Encoding.UTF8.GetString(bufferWriter.WrittenSpan);
    }

    /// <summary>
    /// 写 required_status_checks — 保留 strict, 用新 contexts 构造 checks 数组
    /// </summary>
    private static void WriteRequiredStatusChecks(Utf8JsonWriter writer, JsonElement root, IReadOnlyList<string> newContexts) {
        writer.WritePropertyName("required_status_checks");
        writer.WriteStartObject();
        var strict = root.TryGetProperty("required_status_checks", out var rsc) &&
                     rsc.TryGetProperty("strict", out var strictEl) && strictEl.GetBoolean();
        writer.WriteBoolean("strict", strict);
        writer.WritePropertyName("checks");
        writer.WriteStartArray();
        foreach (var ctx in newContexts) {
            writer.WriteStartObject();
            writer.WriteString("context", ctx);
            writer.WriteEndObject();
        }
        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>
    /// 写 enforce_admins — 从 {enabled: bool} 提取
    /// </summary>
    private static void WriteEnforceAdmins(Utf8JsonWriter writer, JsonElement root) {
        var enabled = root.TryGetProperty("enforce_admins", out var ea) &&
                      ea.TryGetProperty("enabled", out var enEl) && enEl.GetBoolean();
        writer.WriteBoolean("enforce_admins", enabled);
    }

    /// <summary>
    /// 写简单布尔字段 — 从 {enabled: bool} 提取
    /// </summary>
    private static void WriteSimpleBool(Utf8JsonWriter writer, JsonElement root, string name) {
        var enabled = root.TryGetProperty(name, out var el) &&
                      el.TryGetProperty("enabled", out var enEl) && enEl.GetBoolean();
        writer.WriteBoolean(name, enabled);
    }

    /// <summary>
    /// 构建试跑模式摘要 — 显示差异但不实际修改
    /// </summary>
    private static string BuildDryRunSummary(
        string owner, string repo, string branch,
        IReadOnlyList<string> added, IReadOnlyList<string> removed, IReadOnlyList<string> newContexts) {
        var sb = new StringBuilder(512);
        sb.AppendLine($"[试跑] 分支保护同步预览: {owner}/{repo} 分支 '{branch}'");
        sb.AppendLine();
        if (added.Count > 0) {
            sb.AppendLine($"新增 {added.Count} 个 check:");
            foreach (var name in added) sb.AppendLine($"  + {name}");
        }
        if (removed.Count > 0) {
            sb.AppendLine($"移除 {removed.Count} 个 check:");
            foreach (var name in removed) sb.AppendLine($"  - {name}");
        }
        sb.AppendLine($"同步后 required_status_checks 将包含 {newContexts.Count} 个 check:");
        foreach (var ctx in newContexts) sb.AppendLine($"  - {ctx}");
        return sb.ToString();
    }

    /// <summary>
    /// 构建同步结果摘要 — 显示实际修改结果
    /// </summary>
    private static string BuildSyncResultSummary(
        string owner, string repo, string branch,
        IReadOnlyList<string> added, IReadOnlyList<string> removed, IReadOnlyList<string> newContexts) {
        var sb = new StringBuilder(512);
        sb.AppendLine($"✅ 分支保护已同步: {owner}/{repo} 分支 '{branch}'");
        sb.AppendLine();
        if (added.Count > 0) {
            sb.AppendLine($"新增 {added.Count} 个 check:");
            foreach (var name in added) sb.AppendLine($"  + {name}");
        }
        if (removed.Count > 0) {
            sb.AppendLine($"移除 {removed.Count} 个 check:");
            foreach (var name in removed) sb.AppendLine($"  - {name}");
        }
        sb.AppendLine();
        sb.AppendLine($"同步后 required_status_checks ({newContexts.Count} 个):");
        foreach (var ctx in newContexts) sb.AppendLine($"  - {ctx}");
        return sb.ToString();
    }
}