namespace Core.Agents;

/// <summary>
/// 子智能体信封状态 — L0 XML state 属性值
/// </summary>
public enum SubAgentEnvelopeState
{
    Completed,
    Error,
}

/// <summary>
/// 子智能体输出信封 — L0 结构化 XML 包装
/// <para>对齐 openCode renderOutput: task/summary/task_result XML 标签。</para>
/// <para>content 不转义（LLM 伪 XML，对齐 openCode），agentId/summary 转义防注入。</para>
/// </summary>
public static class SubAgentOutputEnvelope
{
    private const int MaxSummaryChars = 100;

    /// <summary>
    /// 包装子智能体输出为结构化 XML
    /// </summary>
    /// <param name="agentId">子智能体标识</param>
    /// <param name="state">完成/错误状态</param>
    /// <param name="summary">一句话概要（可选，null 则省略 summary 标签）</param>
    /// <param name="content">输出正文（O / 自摘要 S / 落盘指针）</param>
    public static string Wrap(string agentId, SubAgentEnvelopeState state, string? summary, string content)
    {
        var stateStr = state == SubAgentEnvelopeState.Error ? "error" : "completed";
        var summaryLine = string.IsNullOrWhiteSpace(summary) ? "" : $"<summary>{EscapeXml(summary)}</summary>\n";
        return $"<task id=\"{EscapeXml(agentId)}\" state=\"{stateStr}\">\n{summaryLine}<task_result>\n{content}\n</task_result>\n</task>";
    }

    /// <summary>
    /// 从子智能体完整输出提取一句话概要 — 取首行,限长 MaxSummaryChars
    /// </summary>
    public static string? ExtractSummary(string output)
    {
        if (string.IsNullOrEmpty(output)) return null;
        var nlIdx = output.IndexOf('\n');
        var firstLine = nlIdx >= 0 ? output[..nlIdx] : output;
        var trimmed = firstLine.Trim();
        if (trimmed.Length == 0) return null;
        if (trimmed.Length <= MaxSummaryChars) return trimmed;
        return trimmed[..MaxSummaryChars] + "…";
    }

    private static string EscapeXml(string value)
    {
        return value
            .Replace("&", "&amp;")
            .Replace("<", "&lt;")
            .Replace(">", "&gt;")
            .Replace("\"", "&quot;");
    }
}
