namespace McpToolDispatch;

/// <summary>
/// 超图调试工具 — 查看工具评分、超边状态、链路推荐
/// 用于诊断工具评分异常、超图压制问题、链路断裂等
/// </summary>
[McpToolDispatch(ToolCategory.Analytics)]
public class ToolScoreDebugToolHandlers
{
    private readonly IToolHealthMonitor _monitor;
    private readonly ToolHypergraphScorer _scorer;
    private readonly ILogger<ToolScoreDebugToolHandlers>? _logger;

    public ToolScoreDebugToolHandlers(
        IToolHealthMonitor monitor,
        ToolHypergraphScorer scorer,
        ILogger<ToolScoreDebugToolHandlers>? logger = null)
    {
        _monitor = monitor;
        _scorer = scorer;
        _logger = logger;
    }

    /// <summary>
    /// 查看工具评分 — 显示独立评分、超图评分、有效评分、黑名单/降权状态
    /// </summary>
    [McpTool("tool_score", "查看工具的评分状态（独立评分+超图评分+有效评分+黑名单/降权）", "tool_debug",
        ConcurrencySafe = true)]
    public async Task<ToolResult> GetToolScoreAsync(
        [McpToolParameter("工具名称（留空则显示所有工具）", Required = false)] string? toolName,
        CancellationToken ct = default)
    {
        var allRecords = await _monitor.GetAllRecordsAsync(ct).ConfigureAwait(false);

        if (!string.IsNullOrWhiteSpace(toolName))
        {
            var record = await _monitor.GetRecordAsync(toolName, ct).ConfigureAwait(false);
            var effectiveScore = _monitor.GetEffectiveScore(toolName);
            var penalty = _monitor.GetPenalty(toolName);
            var isBlacklisted = _monitor.IsBlacklisted(toolName);
            var hypergraphScore = record is not null ? _scorer.CalculateFinalScore(toolName, record.Score) : 0;
            var edges = _scorer.GetEdges(toolName);
            var chain = _scorer.GetChainRecommendations(toolName);

            var sb = new StringBuilder(512);
            sb.AppendLine($"## 工具评分: {toolName}");
            sb.AppendLine($"- 黑名单: {(isBlacklisted ? "是" : "否")}");
            sb.AppendLine($"- 降权: {penalty}");
            sb.AppendLine($"- 独立评分: {record?.Score ?? 0}");
            sb.AppendLine($"- 超图评分: {hypergraphScore}");
            sb.AppendLine($"- 有效评分: {effectiveScore}");
            sb.AppendLine($"- 成功/失败: {record?.SuccessCount ?? 0}/{record?.FailCount ?? 0}");
            sb.AppendLine($"- 成功率: {record?.SuccessRate:P1 ?? 0:P1}");
            sb.AppendLine($"- 熔断: {(record?.IsEnabled == false ? "是" : "否")}");
            sb.AppendLine($"- 连续失败: {record?.ConsecutiveFailures ?? 0}");
            if (record?.LastErrorMessage is not null)
                sb.AppendLine($"- 最后错误: {record.LastErrorMessage}");

            if (edges.Count > 0)
            {
                sb.AppendLine("### 所属超边:");
                foreach (var edge in edges)
                    sb.AppendLine($"- {edge.Id} (权重={edge.Weight}, 共享评分={edge.SharedScore})");
            }

            if (chain is not null && chain.Length > 0)
                sb.AppendLine($"### 链路推荐: {string.Join(" → ", chain)}");

            return ToolResultBuilder.Success().WithText(sb.ToString().TrimEnd()).Build();
        }

        return await BuildAllToolsReportAsync(allRecords, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 查看超图结构 — 显示所有超边及其成员工具的评分
    /// </summary>
    [McpTool("tool_hypergraph", "查看工具链超图结构（所有超边+成员评分+链路顺序）", "tool_debug",
        ConcurrencySafe = true)]
    public async Task<ToolResult> GetHypergraphAsync(CancellationToken ct = default)
    {
        var allRecords = await _monitor.GetAllRecordsAsync(ct).ConfigureAwait(false);
        _scorer.UpdateSharedScores(allRecords);

        var sb = new StringBuilder(1024);
        sb.AppendLine("## 工具链超图");

        var presets = ToolHypergraphPresets.GetPresets();
        sb.AppendLine($"超边总数: {presets.Length}");
        sb.AppendLine();

        foreach (var edge in presets)
        {
            sb.AppendLine($"### {edge.Id} (权重={edge.Weight}, 共享评分={edge.SharedScore})");
            sb.AppendLine($"- 成员工具: {string.Join(", ", edge.ToolNames)}");
            if (edge.ChainOrder is not null)
                sb.AppendLine($"- 链路顺序: {string.Join(" → ", edge.ChainOrder)}");

            var memberScores = edge.ToolNames
                .Select(t => allRecords.TryGetValue(t, out var r) ? $"{t}={r.Score}" : $"{t}=0")
                .ToList();
            sb.AppendLine($"- 成员评分: {string.Join(", ", memberScores)}");
            sb.AppendLine();
        }

        return ToolResultBuilder.Success().WithText(sb.ToString().TrimEnd()).Build();
    }

    /// <summary>
    /// 重置工具评分 — 清除指定工具的健康记录，恢复到初始状态
    /// </summary>
    [McpTool("tool_score_reset", "重置工具评分（清除健康记录，恢复初始状态）", "tool_debug")]
    public async Task<ToolResult> ResetToolScoreAsync(
        [McpToolParameter("工具名称", Required = true)] string toolName,
        CancellationToken ct = default)
    {
        await _monitor.ResetToolAsync(toolName, ct).ConfigureAwait(false);
        return ToolResultBuilder.Success().WithText($"工具 '{toolName}' 评分已重置").Build();
    }

    private async Task<ToolResult> BuildAllToolsReportAsync(
        IReadOnlyDictionary<string, ToolHealthRecord> allRecords, CancellationToken ct)
    {
        if (allRecords.Count == 0)
            return ToolResultBuilder.Success().WithText("暂无工具评分记录").Build();

        var sb = new StringBuilder(2048);
        sb.AppendLine("## 所有工具评分");
        sb.AppendLine();
        sb.AppendLine("| 工具 | 独立评分 | 超图评分 | 有效评分 | 降权 | 成功/失败 | 熔断 |");
        sb.AppendLine("|------|---------|---------|---------|------|----------|------|");

        foreach (var kvp in allRecords.OrderByDescending(k => _monitor.GetEffectiveScore(k.Key)))
        {
            var name = kvp.Key;
            var record = kvp.Value;
            var hyperScore = _scorer.CalculateFinalScore(name, record.Score);
            var effectiveScore = _monitor.GetEffectiveScore(name);
            var penalty = _monitor.GetPenalty(name);
            var penaltyStr = penalty != 0 ? penalty.ToString() : "-";
            var circuitBreaker = !record.IsEnabled ? "是" : "-";

            sb.AppendLine($"| {name} | {record.Score} | {hyperScore} | {effectiveScore} | {penaltyStr} | {record.SuccessCount}/{record.FailCount} | {circuitBreaker} |");
        }

        var blacklisted = allRecords.Keys.Where(k => _monitor.IsBlacklisted(k)).ToList();
        if (blacklisted.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine($"黑名单工具: {string.Join(", ", blacklisted)}");
        }

        return ToolResultBuilder.Success().WithText(sb.ToString().TrimEnd()).Build();
    }
}
