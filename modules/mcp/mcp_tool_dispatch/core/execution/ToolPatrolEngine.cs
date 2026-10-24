namespace McpToolDispatch;

/// <summary>
/// 工具巡检引擎 — 遍历所有已注册工具，走完整 16 层管道调用，助记符验收每个工具，
/// 结果回写 <see cref="IToolHealthMonitor"/>。供 ToolPatrolService 定时调用 + 测试复用。
/// <para>跳过清单覆盖所有 gh_*/git_* 前缀 + desktop 类别 + 网络连接工具，避免真实写入副作用。</para>
/// <para>每调用 30s 超时守卫，检测 ActorAskDeadlockException/TimeoutException 单独记 [LOCK]/[TMO]。</para>
/// </summary>
[Register(typeof(IToolPatrolEngine), ServiceLifetime.Singleton)]
public sealed class ToolPatrolEngine : IToolPatrolEngine {
    private readonly IToolExecutionGateway _gateway;
    private readonly IMcpToolRegistry _registry;
    private readonly IToolHealthMonitor _monitor;
    private readonly ILogger<ToolPatrolEngine>? _logger;

    /// <summary>每工具调用超时（30s）— 超过记 [TMO]。</summary>
    private const int PerToolTimeoutMs = 30_000;

    /// <summary>有真实 git 副作用的工具 — 巡检跳过（worktree 操作非 git_ 前缀，单独列出）。</summary>
    private static readonly FrozenSet<string> ToolsWithGitSideEffects = FrozenSet.Create(
        StringComparer.Ordinal,
        [
            "worktree_create", "worktree_remove", "worktree_cleanup", "worktree_merge",
            "enter_worktree", "exit_worktree",
        ]);

    /// <summary>有真实网络副作用的工具 — 巡检跳过（mcp_connect 连真实 MCP 服务器）。</summary>
    private static readonly FrozenSet<string> ToolsWithNetworkSideEffects = FrozenSet.Create(
        StringComparer.Ordinal, ["mcp_connect"]);

    /// <summary>
    /// 构造工具巡检引擎 — 注入执行网关、工具注册表、健康监控、日志。
    /// </summary>
    public ToolPatrolEngine(
        IToolExecutionGateway gateway,
        IMcpToolRegistry registry,
        IToolHealthMonitor monitor,
        ILogger<ToolPatrolEngine>? logger = null) {
        _gateway = gateway;
        _registry = registry;
        _monitor = monitor;
        _logger = logger;
    }

    /// <summary>
    /// 执行一轮全工具巡检 — 枚举所有工具，跳过有副作用的，走完整管道调用，助记符验收，回写健康监控。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>巡检报告（含每个工具助记符结果）</returns>
    public async Task<ToolPatrolReport> PatrolAsync(CancellationToken ct = default) {
        var tools = await _registry.GetAllToolInfosAsync(ct).ConfigureAwait(false);
        var results = new List<ToolPatrolResult>(tools.Count);

        foreach (var tool in tools.OrderBy(t => t.Name)) {
            if (ct.IsCancellationRequested) break;
            results.Add(await PatrolOneAsync(tool, ct).ConfigureAwait(false));
        }

        var report = new ToolPatrolReport(DateTimeOffset.UtcNow, tools.Count, results);
        _logger?.LogInformation("[ToolPatrol] 巡检完成: {Total} 工具, [OK]{Ok} [ERR]{Err} [CRASH]{Crash} [LOCK]{Lock} [TMO]{Tmo} [SKIP]{Skip}",
            report.TotalTools, report.OkCount, report.ErrorCount, report.CrashCount, report.DeadlockCount, report.TimeoutCount, report.SkippedCount);
        return report;
    }

    private async Task<ToolPatrolResult> PatrolOneAsync(ToolInfo tool, CancellationToken ct) {
        if (HasRealSideEffects(tool)) {
            return new ToolPatrolResult(tool.Name, ToolPatrolStatus.Skipped, "有真实副作用", TimeSpan.Zero);
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        try {
            var args = BuildMinimalArguments(tool);
            using var cts = new CancellationTokenSource(PerToolTimeoutMs);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, cts.Token);
            var result = await _gateway.ExecuteAsync(tool.Name, args, linked.Token).ConfigureAwait(false);

            var errorMsg = result.IsError ? result.Content.FirstOrDefault()?.Text : null;
            sw.Stop();
            // 回写健康监控（fire-and-forget，不阻塞巡检）
            if (result.IsError) {
                _ = _monitor.RecordFailureAsync(tool.Name, errorMsg, ct);
            } else {
                _ = _monitor.RecordSuccessAsync(tool.Name, ct);
            }
            return new ToolPatrolResult(tool.Name, result.IsError ? ToolPatrolStatus.Error : ToolPatrolStatus.Ok, errorMsg, sw.Elapsed);
        } catch (ActorAskDeadlockException ex) {
            sw.Stop();
            return new ToolPatrolResult(tool.Name, ToolPatrolStatus.Deadlock, ex.Message, sw.Elapsed);
        } catch (OperationCanceledException ex) when (!ct.IsCancellationRequested) {
            sw.Stop();
            return new ToolPatrolResult(tool.Name, ToolPatrolStatus.Timeout, $"30s 超时: {ex.Message}", sw.Elapsed);
        } catch (TimeoutException ex) {
            sw.Stop();
            return new ToolPatrolResult(tool.Name, ToolPatrolStatus.Timeout, ex.Message, sw.Elapsed);
        } catch (Exception ex) {
            sw.Stop();
            return new ToolPatrolResult(tool.Name, ToolPatrolStatus.Crash, $"{ex.GetType().Name}: {ex.Message}", sw.Elapsed);
        }
    }

    /// <summary>
    /// 判断工具是否有真实副作用（desktop/gh_*/git_*/worktree/network/agent/LLM），巡检应跳过。
    /// <para>gh_* 全跳：GitHub 写入副作用（create issue/PR/comment、cancel/rerun run、fork repo 等）。</para>
    /// <para>git_* 全跳：git 本地仓库副作用（add/commit/push/branch/clone/reset/clean 等）。</para>
    /// <para>agent_* + forward_user_input 全跳：创建子代理/转发输入会真实调 LLM 浪费 token。</para>
    /// </summary>
    public static bool HasRealSideEffects(ToolInfo tool) =>
        string.Equals(tool.Category, "desktop", StringComparison.OrdinalIgnoreCase)
        || ToolsWithGitSideEffects.Contains(tool.Name)
        || ToolsWithNetworkSideEffects.Contains(tool.Name)
        || tool.Name.StartsWith("gh_", StringComparison.Ordinal)
        || tool.Name.StartsWith("git_", StringComparison.Ordinal)
        || tool.Name.StartsWith("agent_", StringComparison.Ordinal)
        || string.Equals(tool.Name, "agent", StringComparison.Ordinal)
        || string.Equals(tool.Name, "forward_user_input", StringComparison.Ordinal);

    /// <summary>
    /// 为工具构造最小参数集 — 对必需参数提供最小有效值（按参数名+类型启发式）。
    /// </summary>
    public static Dictionary<string, JsonElement> BuildMinimalArguments(ToolInfo tool) {
        var args = new Dictionary<string, JsonElement>();
        if (tool.InputSchema?.Required is null) return args;

        foreach (var reqParam in tool.InputSchema.Required) {
            if (!tool.InputSchema.Properties.TryGetValue(reqParam, out var prop)) continue;
            args[reqParam] = BuildMinimalValue(reqParam, prop.Type);
        }
        return args;
    }

    private static JsonElement BuildMinimalValue(string paramName, string propType) {
        var json = propType switch {
            "string" => paramName switch {
                "file_path" or "path" => "\"test.txt\"",
                "directory_path" => "\".\"",
                "command" => "\"echo hello\"",
                "pattern" => "\"*.txt\"",
                "query" or "prompt" or "code" or "expression" or "action" or "target"
                    or "agent_name" or "cassette_name" or "client_id" or "tool_name"
                    or "username" or "password" or "api_key" or "token" or "refresh_token"
                    or "client_secret" or "schema_name" or "auth_id" => "\"test\"",
                "url" or "auth_url" or "token_url" => "\"http://localhost\"",
                "schema_json" => "\"{}\"",
                "agent_pattern" => "\"*\"",
                "resource_uri" => "\"test://resource\"",
                "confirm" => "\"yes\"",
                _ => "\"test\"",
            },
            "integer" => "1",
            "boolean" => "false",
            "array" => "[]",
            "object" => "{}",
            _ => "\"test\"",
        };
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    /// <summary>
    /// 格式化巡检报告为文本（助记符每行前缀）— 供日志/持久化/控制台输出。
    /// </summary>
    public static string FormatReport(ToolPatrolReport report) {
        var sb = new StringBuilder();
        sb.AppendLine("# 工具巡检报告（走完整 16 层管道 + 30s 超时守卫）");
        sb.AppendLine($"时间: {report.Timestamp:O}");
        sb.AppendLine($"工具总数: {report.TotalTools}");
        sb.AppendLine($"[OK]{report.OkCount} [ERR]{report.ErrorCount} [CRASH]{report.CrashCount} [LOCK]{report.DeadlockCount} [TMO]{report.TimeoutCount} [SKIP]{report.SkippedCount}");
        sb.AppendLine();
        foreach (var r in report.Results.OrderBy(r => r.ToolName)) {
            var truncated = r.ErrorMessage switch {
                null => "",
                { Length: > 100 } => r.ErrorMessage[..100] + "...",
                _ => r.ErrorMessage,
            };
            sb.AppendLine($"{r.Mnemonic} {r.ToolName} {truncated}");
        }
        return sb.ToString();
    }
}
