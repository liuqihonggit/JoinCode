
namespace Core.Context.Collapse;

/// <summary>
/// 可折叠的上下文段
/// </summary>
public sealed class CollapsibleSegment {
    /// <summary>
    /// 段唯一标识
    /// </summary>
    public required string Id { get; init; }
    /// <summary>
    /// 段原始内容
    /// </summary>
    public required string Content { get; init; }
    /// <summary>
    /// 段类型
    /// </summary>
    public required CollapsibleSegmentType Type { get; init; }
    /// <summary>
    /// 段在原文中的起始偏移
    /// </summary>
    public int StartOffset { get; init; }
    /// <summary>
    /// 段在原文中的结束偏移
    /// </summary>
    public int EndOffset { get; init; }
    /// <summary>
    /// 段的估算 token 数
    /// </summary>
    public int TokenCount { get; init; }
    /// <summary>
    /// 折叠优先级（0-1，越大越优先折叠）
    /// </summary>
    public double CollapsePriority { get; init; }
    /// <summary>
    /// 段中提取的关键引用标识
    /// </summary>
    public IReadOnlyList<string> KeyReferences { get; init; } = Array.Empty<string>();
    /// <summary>
    /// 段附加元数据
    /// </summary>
    public Dictionary<string, string> Metadata { get; init; } = new();
}

/// <summary>
/// 可折叠段的类型
/// </summary>
public enum CollapsibleSegmentType {
    /// <summary>
    /// 代码块
    /// </summary>
    [EnumValue("codeBlock")] CodeBlock,
    /// <summary>
    /// 重复模式
    /// </summary>
    [EnumValue("repetitivePattern")] RepetitivePattern,
    /// <summary>
    /// 历史对话
    /// </summary>
    [EnumValue("historicalDialogue")] HistoricalDialogue,
    /// <summary>
    /// 工具输出
    /// </summary>
    [EnumValue("toolOutput")] ToolOutput,
    /// <summary>
    /// 长散文
    /// </summary>
    [EnumValue("longProse")] LongProse,
    /// <summary>
    /// 系统消息
    /// </summary>
    [EnumValue("systemMessage")] SystemMessage
}