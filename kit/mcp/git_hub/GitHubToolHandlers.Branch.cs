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
    [McpTool(GitHubToolNameEnumConstants.GhBranchSyncProtection, "同步分支保护: 从 CI yml matrix 读取 job 名, 对比 required_status_checks, PUT 完整 protection 端点同步差异", "github")]
    public async Task<ToolResult> GhBranchSyncProtectionAsync(
        [McpToolParameter("分支名(默认 main)", Required = false)] string? branch = null,
        [McpToolParameter("CI yml 路径(默认 .github/workflows/ci-unit-tests.yml)", Required = false)] string? yml_path = null,
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        [McpToolParameter("试跑模式(只显示差异不实际修改)", Required = false)] bool? dry_run = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var branchName = string.IsNullOrWhiteSpace(branch) ? "main" : branch;
            var ymlPath = string.IsNullOrWhiteSpace(yml_path) ? ".github/workflows/ci-unit-tests.yml" : yml_path;
            var workingDir = string.IsNullOrWhiteSpace(working_dir) ? Environment.CurrentDirectory : working_dir;
            var fullPath = Path.Combine(workingDir, ymlPath);
            var isDryRun = dry_run == true;

            var auditor = new BranchProtectionAuditor(client, _fs);
            var audit = await auditor.AuditAsync(owner, repoName, branchName, fullPath, cancellationToken).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(audit.Note) && audit.CiJobNames.Count == 0)
                return Fail(audit.Note);
            if (audit.IsConsistent)
                return Ok($"✅ CI matrix 与分支保护完全一致, 无需同步\n\n{audit.BuildReport()}");

            if (!_fs.FileExists(fullPath))
                return Fail($"CI yml 文件不存在: {fullPath}");
            var ymlContent = await _fs.ReadAllTextAsync(fullPath, cancellationToken).ConfigureAwait(false);
            var jobId = CiMatrixParser.ExtractJobId(ymlContent) ?? "unit-tests";

            var protectionResult = await client.SendAsync(
                HttpMethod.Get, $"repos/{owner}/{repoName}/branches/{branchName}/protection",
                ct: cancellationToken).ConfigureAwait(false);
            if (!protectionResult.Success)
                return Fail($"获取分支保护规则失败: {protectionResult.Error}");

            var newContexts = BuildSyncedContexts(protectionResult.Body, jobId, audit);
            var putBody = BuildFullProtectionPutBody(protectionResult.Body, newContexts);
            if (isDryRun)
                return Ok(BuildDryRunSummary(owner, repoName, branchName, audit, newContexts));

            var putResult = await client.SendAsync(
                HttpMethod.Put, $"repos/{owner}/{repoName}/branches/{branchName}/protection",
                body: putBody, ct: cancellationToken).ConfigureAwait(false);
            if (!putResult.Success)
                return Fail($"PUT 分支保护规则失败: {putResult.Error}");

            return Ok(BuildSyncResultSummary(owner, repoName, branchName, audit, newContexts));
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
        [McpToolParameter("仓库(可选,默认当前仓库)", Required = false)] string? repo = null,
        [McpToolParameter("工作目录(可选)", Required = false)] string? working_dir = null,
        CancellationToken cancellationToken = default)
        => await ExecuteGhAsync(repo, working_dir, cancellationToken, async (client, owner, repoName) => {
            var branchName = string.IsNullOrWhiteSpace(branch) ? "main" : branch;
            var ymlPath = string.IsNullOrWhiteSpace(yml_path) ? ".github/workflows/ci-unit-tests.yml" : yml_path;
            var workingDir = string.IsNullOrWhiteSpace(working_dir) ? Environment.CurrentDirectory : working_dir;
            var fullPath = Path.Combine(workingDir, ymlPath);
            var auditor = new BranchProtectionAuditor(client, _fs);
            var result = await auditor.AuditAsync(owner, repoName, branchName, fullPath, cancellationToken).ConfigureAwait(false);
            return Ok(result.BuildReport());
        }).ConfigureAwait(false);

    /// <summary>
    /// 从完整 protection JSON 构造同步后的 contexts 列表 — 保留其他 workflow 的 check, 替换目标 workflow 的 check
    /// <para>逻辑: 从 protection.required_status_checks.contexts 提取所有 check 名, 筛选出非目标 workflow 的(保留),
    /// 加上 CI matrix 对应的新 check 名(从现有 check 名提取前缀模板 + matrix name 构造)</para>
    /// </summary>
    internal static List<string> BuildSyncedContexts(
        string protectionJson, string jobId, BranchProtectionAuditResult audit) {
        List<string> allContexts;
        using (var doc = JsonDocument.Parse(protectionJson)) {
            allContexts = ExtractContexts(doc.RootElement);
        }

        var prefix = $"{jobId} / ";
        var otherWorkflowChecks = allContexts
            .Where(c => !c.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();

        var targetChecks = allContexts
            .Where(c => c.StartsWith(prefix, StringComparison.Ordinal))
            .ToList();

        var checkPrefix = InferCheckPrefix(targetChecks, prefix, audit.CiJobNames);

        var newTargetChecks = audit.CiJobNames
            .Select(name => $"{checkPrefix}{name}")
            .ToList();

        return [.. otherWorkflowChecks, .. newTargetChecks];
    }

    /// <summary>
    /// 从现有 check 名推断前缀模板 — 如 "unit-tests / Unit - Abs" → "unit-tests / Unit - "
    /// <para>若无现有 check, 用默认格式 "{jobId} / Unit - "(从 CI yml job name 模板推断)</para>
    /// </summary>
    internal static string InferCheckPrefix(
        IReadOnlyList<string> existingChecks, string jobIdPrefix, IReadOnlyList<string> ciJobNames) {
        if (existingChecks.Count > 0) {
            var sample = existingChecks[0];
            var matrixName = CiMatrixParser.ExtractMatrixNameFromCheck(sample, jobIdPrefix[..^2]);
            if (matrixName is not null && sample.EndsWith(matrixName, StringComparison.Ordinal))
                return sample[..^matrixName.Length];
        }
        return $"{jobIdPrefix}Unit - ";
    }

    /// <summary>
    /// 从 JsonElement 提取 required_status_checks.contexts 列表
    /// </summary>
    private static List<string> ExtractContexts(JsonElement root) {
        var contexts = new List<string>();
        if (root.TryGetProperty("required_status_checks", out var rsc) &&
            rsc.TryGetProperty("contexts", out var contextsEl)) {
            foreach (var ctx in contextsEl.EnumerateArray()) {
                var name = ctx.GetString();
                if (!string.IsNullOrEmpty(name)) contexts.Add(name);
            }
        }
        return contexts;
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
        BranchProtectionAuditResult audit, IReadOnlyList<string> newContexts) {
        var sb = new StringBuilder(512);
        sb.AppendLine($"[试跑] 分支保护同步预览: {owner}/{repo} 分支 '{branch}'");
        sb.AppendLine();
        sb.AppendLine(audit.BuildReport());
        sb.AppendLine($"同步后 required_status_checks 将包含 {newContexts.Count} 个 check:");
        foreach (var ctx in newContexts) sb.AppendLine($"  - {ctx}");
        return sb.ToString();
    }

    /// <summary>
    /// 构建同步结果摘要 — 显示实际修改结果
    /// </summary>
    private static string BuildSyncResultSummary(
        string owner, string repo, string branch,
        BranchProtectionAuditResult audit, IReadOnlyList<string> newContexts) {
        var sb = new StringBuilder(512);
        sb.AppendLine($"✅ 分支保护已同步: {owner}/{repo} 分支 '{branch}'");
        sb.AppendLine();
        if (audit.MissingFromProtection.Count > 0) {
            sb.AppendLine($"新增 {audit.MissingFromProtection.Count} 个 check:");
            foreach (var name in audit.MissingFromProtection) sb.AppendLine($"  + {name}");
        }
        if (audit.StaleInProtection.Count > 0) {
            sb.AppendLine($"移除 {audit.StaleInProtection.Count} 个 check:");
            foreach (var name in audit.StaleInProtection) sb.AppendLine($"  - {name}");
        }
        sb.AppendLine();
        sb.AppendLine($"同步后 required_status_checks ({newContexts.Count} 个):");
        foreach (var ctx in newContexts) sb.AppendLine($"  - {ctx}");
        return sb.ToString();
    }
}