namespace McpToolRegistry;

/// <summary>
/// 报错组动态注入中间件 — Order=800 — 工具执行失败时自动注入OnError工具说明
/// OnError工具不出现在首次系统提示词，仅留函数名；首次报错时弹出工具说明让LLM选择
/// 增强功能：历史修复分析 — 从健康记录中提取同类工具的失败模式，提前给出建议
/// </summary>
[Register]
public sealed partial class OnErrorToolInjectionMiddleware : ServiceEntity, IToolExecutionMiddleware
{
    private readonly IToolRegistry _registry;
    private readonly IToolHealthMonitor _monitor;
    private readonly ToolHypergraphScorer _scorer;
    private readonly ILogger<OnErrorToolInjectionMiddleware> _logger;

    public OnErrorToolInjectionMiddleware(
        IToolRegistry registry,
        IToolHealthMonitor monitor,
        ToolHypergraphScorer scorer,
        ILogger<OnErrorToolInjectionMiddleware> logger)
    {
        _registry = registry;
        _monitor = monitor;
        _scorer = scorer;
        _logger = logger;
    }

    public ErrorBehavior OnError => ErrorBehavior.Continue;

    public async Task InvokeAsync(ToolExecutionContext context, MiddlewareDelegate<ToolExecutionContext> next, CancellationToken ct)
    {
        await next(context, ct).ConfigureAwait(false);

        if (context.Result is null || !context.Result.IsError) return;

        var sb = new StringBuilder(1024);

        sb.AppendLine($"工具 '{context.ToolName}' 执行失败。");

        // 历史修复分析 — 从健康记录中提取同类工具的失败模式
        var historyAnalysis = await BuildHistoryAnalysisAsync(context, ct).ConfigureAwait(false);
        if (historyAnalysis is not null)
            sb.AppendLine(historyAnalysis);

        // OnError 工具推荐 — 强行注入完整 schema（渐进式暴露：从自行探索变成强行注入单个）
        var onErrorTools = await _registry.GetToolsByKindAsync(ToolKind.OnError, ct).ConfigureAwait(false);
        if (onErrorTools.Count > 0)
        {
            var relevantTools = FindRelevantOnErrorTools(context.ToolName, onErrorTools);
            if (relevantTools.Count > 0)
            {
                sb.AppendLine("以下修复工具可用（完整定义如下，可直接调用）：");
                foreach (var tool in relevantTools.Values)
                {
                    sb.AppendLine(BuildToolSchemaJson(tool));
                }
            }
        }

        // 超图链路推荐 — 推荐关联工具作为替代
        var chainRecommendations = _scorer.GetChainRecommendations(context.ToolName);
        if (chainRecommendations is not null && chainRecommendations.Length > 0)
        {
            sb.AppendLine($"推荐替代工具链路: {string.Join(" → ", chainRecommendations)}");
        }

        sb.AppendLine("请选择合适的修复工具，或尝试其他方式解决问题。");

        var injection = new JoinCode.Abstractions.LLM.Chat.ApiMessage(
            JoinCode.Abstractions.LLM.Chat.MessageRole.User, sb.ToString());
        context.Result = context.Result with
        {
            InjectedMessages = [.. (context.Result.InjectedMessages ?? []), injection]
        };

        _logger?.LogDebug("已注入错误修复建议到上下文（含历史分析+OnError工具+链路推荐）");
    }

    /// <summary>
    /// 历史修复分析 — 从健康记录中提取同类工具的失败模式
    /// 分析维度：1) 同工具历史失败率 2) 同超边关联工具状态 3) 常见错误模式
    /// </summary>
    private async Task<string?> BuildHistoryAnalysisAsync(ToolExecutionContext context, CancellationToken ct)
    {
        var toolName = context.ToolName;
        var errorMsg = context.Result?.GetFirstText();
        var record = await _monitor.GetRecordAsync(toolName, ct).ConfigureAwait(false);
        var allRecords = await _monitor.GetAllRecordsAsync(ct).ConfigureAwait(false);

        var sb = new StringBuilder(512);
        var hasAnalysis = false;

        // 1. 同工具历史失败率
        if (record is not null && (record.SuccessCount + record.FailCount) > 0)
        {
            var failRate = (double)record.FailCount / (record.SuccessCount + record.FailCount);
            if (failRate > 0.3)
            {
                sb.AppendLine($"### 历史分析: '{toolName}' 失败率 {failRate:P0}（成功{record.SuccessCount}次/失败{record.FailCount}次）");
                if (!string.IsNullOrEmpty(record.LastErrorMessage))
                    sb.AppendLine($"- 上次错误: {record.LastErrorMessage}");
                hasAnalysis = true;
            }
        }

        // 2. 同超边关联工具状态 — 检查关联工具是否也有问题
        var edges = _scorer.GetEdges(toolName);
        if (edges.Count > 0)
        {
            var problematicPeers = new List<string>();
            foreach (var edge in edges)
            {
                foreach (var peer in edge.ToolNames)
                {
                    if (string.Equals(peer, toolName, StringComparison.OrdinalIgnoreCase)) continue;
                    if (allRecords.TryGetValue(peer, out var peerRecord) && !peerRecord.IsEnabled)
                    {
                        problematicPeers.Add($"{peer}（已熔断，评分{peerRecord.Score}）");
                    }
                }
            }

            if (problematicPeers.Count > 0)
            {
                sb.AppendLine("### 关联工具异常:");
                foreach (var peer in problematicPeers)
                    sb.AppendLine($"- {peer}");
                hasAnalysis = true;
            }
        }

        // 3. 常见错误模式匹配 — 从所有工具的历史错误中找相似模式
        if (!string.IsNullOrEmpty(errorMsg))
        {
            var similarErrors = new List<string>();
            foreach (var kvp in allRecords)
            {
                if (string.Equals(kvp.Key, toolName, StringComparison.OrdinalIgnoreCase)) continue;
                if (kvp.Value.LastErrorMessage is not null &&
                    HasSimilarErrorPattern(errorMsg, kvp.Value.LastErrorMessage))
                {
                    similarErrors.Add($"{kvp.Key}: {kvp.Value.LastErrorMessage}");
                }
            }

            if (similarErrors.Count > 0)
            {
                sb.AppendLine("### 相似错误模式（其他工具也遇到过）:");
                foreach (var err in similarErrors.Take(3))
                    sb.AppendLine($"- {err}");
                hasAnalysis = true;
            }
        }

        return hasAnalysis ? sb.ToString() : null;
    }

    /// <summary>
    /// 简单错误模式相似度检测 — 提取关键词匹配
    /// </summary>
    private static bool HasSimilarErrorPattern(string error1, string error2)
    {
        var keywords1 = ExtractErrorKeywords(error1);
        var keywords2 = ExtractErrorKeywords(error2);

        var commonCount = keywords1.Intersect(keywords2, StringComparer.OrdinalIgnoreCase).Count();
        return commonCount >= 2;
    }

    private static string[] ExtractErrorKeywords(string error)
    {
        var separators = new[] { ' ', ':', ';', ',', '.', '(', ')', '[', ']', '{', '}', '\'', '"', '\n', '\r' };
        var words = error.Split(separators, StringSplitOptions.RemoveEmptyEntries);
        return words
            .Where(w => w.Length > 3)
            .Take(10)
            .ToArray();
    }

    /// <summary>
    /// 构建工具完整 schema JSON — 格式对齐 OpenAI function calling tool 定义
    /// </summary>
    private static string BuildToolSchemaJson(IToolHandler tool)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "function");
            writer.WritePropertyName("function");
            writer.WriteStartObject();
            writer.WriteString("name", tool.Name);
            writer.WriteString("description", tool.Description);
            writer.WritePropertyName("parameters");
            JsonSerializer.Serialize(writer, tool.InputSchema, ContractsJsonContext.Default.ToolSchema);
            writer.WriteEndObject();
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static Dictionary<string, IToolHandler> FindRelevantOnErrorTools(
        string failedToolName,
        IReadOnlyDictionary<string, IToolHandler> onErrorTools)
    {
        var result = new Dictionary<string, IToolHandler>(StringComparer.OrdinalIgnoreCase);

        foreach (var tool in onErrorTools)
        {
            if (tool.Value.GroupName is not null &&
                tool.Value.GroupName.Equals(failedToolName, StringComparison.OrdinalIgnoreCase))
            {
                result[tool.Key] = tool.Value;
            }
        }

        if (result.Count == 0)
        {
            foreach (var tool in onErrorTools)
                result[tool.Key] = tool.Value;
        }

        return result;
    }
}
