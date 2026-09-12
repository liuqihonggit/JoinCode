namespace JoinCode.Abstractions.LLM.Execution;

/// <summary>
/// 将 QueryStreamChunk 映射为显示文本行的纯函数工具。
/// 三套 UI（CLI/TUI/GUI）可共用此映射，避免各写一份事件格式化逻辑。
/// 返回 null 表示该 chunk 无需显示。
/// </summary>
public static class ChunkFormatter
{
    /// <summary>
    /// 将 QueryStreamChunk 映射为显示文本行。返回 null 表示该 chunk 无需显示。
    /// 对齐 GUI MainViewModel 的事件处理（7种事件）和 CLI CliEventConsumer（8种事件）。
    /// </summary>
    /// <param name="chunk">查询流式输出块。</param>
    /// <returns>显示文本行，或 null。</returns>
    public static string? ChunkToText(QueryStreamChunk chunk)
    {
        return chunk.Type switch
        {
            AgentStreamChunkType.Content => chunk.Content,
            AgentStreamChunkType.ThinkingStart => "  [思考开始]",
            AgentStreamChunkType.Thinking => $"  [思考] {chunk.ThinkingContent}",
            AgentStreamChunkType.ThinkingEnd => "  [思考结束]",
            AgentStreamChunkType.ToolCallStart => $"  [工具] {chunk.ToolName}",
            AgentStreamChunkType.ToolCallEnd => FormatToolResult(chunk),
            AgentStreamChunkType.ToolProgress => $"  [进度] {chunk.ProgressMessage}",
            AgentStreamChunkType.LoopDetected => $"  ⚠️ [循环检测] 触发 {chunk.LoopTriggerCount} 次",
            AgentStreamChunkType.TimingSummary => $"  ⏱️ {chunk.Content}",
            AgentStreamChunkType.Complete => FormatComplete(chunk),
            AgentStreamChunkType.Error => $"  [错误] {chunk.Content}",
            _ => null,
        };
    }

    private static string FormatToolResult(QueryStreamChunk chunk)
    {
        var status = chunk.IsToolError ? "❌" : "✅";
        var message = ToolErrorFormatter.ExtractMessage(chunk.ToolResultText, chunk.IsToolError);
        var maxLen = chunk.IsToolError ? 500 : 200;
        var result = TruncateText(message, maxLen);
        return $"  [工具] {chunk.ToolName} {status} {result}";
    }

    private static string FormatComplete(QueryStreamChunk chunk)
    {
        if (chunk.Usage is not null)
            return $"  ✅ 完成 │ Token: {chunk.Usage.TotalTokens} │ 模型: {chunk.ModelId}";
        return "  ✅ 完成";
    }

    private static string TruncateText(string? text, int maxLen)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text.Length > maxLen ? string.Concat(text.AsSpan(0, maxLen - 3), "...") : text;
    }
}
