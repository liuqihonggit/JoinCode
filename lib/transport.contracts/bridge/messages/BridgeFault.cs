namespace JoinCode.Transport.Bridge;

/// <summary>
/// 故障类型 — 对齐 TS 端 BridgeFault.kind
/// </summary>
public enum BridgeFaultKind
{
    /// <summary>致命错误（不可重试）</summary>
    [EnumValue("fatal")]
    Fatal,
    /// <summary>瞬态错误（可重试）</summary>
    [EnumValue("transient")]
    Transient,
}

/// <summary>
/// 一次性故障注入描述 — 对齐 TS 端 BridgeFault
/// </summary>
public sealed class BridgeFault
{
    /// <summary>目标 API 方法名</summary>
    public required string Method { get; init; }

    /// <summary>故障类型</summary>
    public required BridgeFaultKind Kind { get; init; }

    /// <summary>模拟的 HTTP 状态码</summary>
    public int Status { get; init; }

    /// <summary>错误类型标识（如 ws_closed_1002）</summary>
    public string? ErrorType { get; init; }

    /// <summary>剩余注入次数</summary>
    public int RemainingCount { get; set; } = 1;
}
