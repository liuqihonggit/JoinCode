
namespace Core.Context.Compact;

/// <summary>
/// 压缩触发方式枚举
/// </summary>
public enum CompactTrigger
{
    /// <summary>手动触发</summary>
    Manual,
    /// <summary>自动触发</summary>
    Auto,
    /// <summary>响应式触发（错误驱动）</summary>
    Reactive
}

/// <summary>
/// 压缩级别枚举
/// </summary>
public enum CompactLevel
{
    /// <summary>未压缩</summary>
    [EnumValue("none")] None,
    /// <summary>微压缩</summary>
    [EnumValue("microcompact")] Microcompact,
    /// <summary>时间间隔微压缩</summary>
    [EnumValue("timeBasedMicrocompact")] TimeBasedMicrocompact,
    /// <summary>会话记忆压缩</summary>
    [EnumValue("sessionMemoryCompact")] SessionMemoryCompact,
    /// <summary>全量压缩</summary>
    [EnumValue("fullCompact")] FullCompact,
    /// <summary>部分压缩</summary>
    [EnumValue("partialCompact")] PartialCompact,
    /// <summary>响应式压缩</summary>
    [EnumValue("reactiveCompact")] ReactiveCompact
}

/// <summary>
/// 压缩结果
/// </summary>
public sealed class CompactResult
{
    /// <summary>是否已压缩</summary>
    public required bool Compacted { get; init; }
    /// <summary>压缩级别</summary>
    public required CompactLevel Level { get; init; }
    /// <summary>压缩触发方式</summary>
    public required CompactTrigger Trigger { get; init; }
    /// <summary>压缩摘要文本</summary>
    public string? Summary { get; init; }
    /// <summary>压缩前 token 数</summary>
    public int PreCompactTokenCount { get; init; }
    /// <summary>压缩后 token 数</summary>
    public int PostCompactTokenCount { get; init; }
    /// <summary>移除的消息数</summary>
    public int MessagesRemoved { get; init; }
    /// <summary>保留的消息数</summary>
    public int MessagesPreserved { get; init; }
    /// <summary>错误信息</summary>
    public string? ErrorMessage { get; init; }
    /// <summary>元数据字典</summary>
    public Dictionary<string, JsonElement> Metadata { get; init; } = new();
    /// <summary>token 节省比例</summary>
    public double TokenSavingsRatio => PreCompactTokenCount > 0
        ? 1.0 - (double)PostCompactTokenCount / PreCompactTokenCount
        : 0;
}
