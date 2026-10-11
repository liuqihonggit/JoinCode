namespace JoinCode.Abstractions.Security.Sandbox;

public sealed partial class SandboxDegradationResult {
    /// <summary>获取请求的沙箱类型。</summary>
    public required SandboxType RequestedType { get; init; }
    /// <summary>获取实际沙箱类型。</summary>
    public required SandboxType ActualType { get; init; }
    /// <summary>获取是否发生降级。</summary>
    public required bool WasDegraded { get; init; }
    /// <summary>获取沙箱信息。</summary>
    public SandboxInfo? Info { get; init; }
    /// <summary>获取降级消息。</summary>
    public string? Message { get; init; }
}