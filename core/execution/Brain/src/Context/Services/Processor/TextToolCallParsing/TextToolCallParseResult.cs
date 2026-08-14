namespace Core.Context;

/// <summary>
/// 文本工具调用解析结果
/// </summary>
public sealed record TextToolCallParseResult
{
    /// <summary>解析出的工具调用列表（至少 1 个）</summary>
    public required IReadOnlyList<ToolCallEntry> ToolCalls { get; init; }

    /// <summary>
    /// 解析成功后剩余的纯文本（工具调用块之前/之后的内容）。
    /// 无剩余文本时为 null；有剩余时用于将纯文本也展示给用户。
    /// </summary>
    public string? RemainingText { get; init; }
}
