namespace JoinCode.Transport;

/// <summary>
/// 传输回退事件参数 — 当传输从一种类型回退到另一种类型时携带的上下文信息
/// </summary>
public sealed class TransportFallbackEventArgs : EventArgs {
    /// <summary>回退前的传输类型名称</summary>
    public required string FromTransportType { get; init; }
    /// <summary>回退后的传输类型名称</summary>
    public required string ToTransportType { get; init; }
    /// <summary>回退原因描述</summary>
    public required string Reason { get; init; }
    /// <summary>是否由服务端触发的回退</summary>
    public required bool IsServerSide { get; init; }
    /// <summary>回退发生的时间戳</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>回退前传输的优先级</summary>
    public int FromPriority { get; init; }
    /// <summary>回退后传输的优先级</summary>
    public int ToPriority { get; init; }
}