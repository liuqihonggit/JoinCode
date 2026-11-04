namespace JoinCode.Abstractions.Security.Sandbox;

public sealed partial class SandboxSwitchResult {
    /// <summary>获取切换前的沙箱类型。</summary>
    public required SandboxType FromType { get; init; }
    /// <summary>获取切换后的沙箱类型。</summary>
    public required SandboxType ToType { get; init; }
    /// <summary>获取是否切换成功。</summary>
    public required bool Success { get; init; }
    /// <summary>获取错误信息。</summary>
    public string? ErrorMessage { get; init; }
    /// <summary>获取新沙箱信息。</summary>
    public SandboxInfo? NewSandboxInfo { get; init; }
}
