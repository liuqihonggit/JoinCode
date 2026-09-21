namespace JoinCode.Abstractions.Security.Sandbox;

public sealed partial class SandboxInfo {
    /// <summary>获取沙箱类型。</summary>
    public required SandboxType Type { get; init; }
    /// <summary>获取沙箱标识。</summary>
    public required string SandboxId { get; init; }
    /// <summary>获取沙箱根路径。</summary>
    public required string RootPath { get; init; }
    /// <summary>获取进入沙箱的时间。</summary>
    public required DateTime EnteredAt { get; init; }
    /// <summary>获取是否为受限沙箱。</summary>
    public required bool IsRestricted { get; init; }
    /// <summary>获取沙箱能力配置。</summary>
    public SandboxCapabilities Capabilities { get; init; }
    /// <summary>获取沙箱大小(字节)。</summary>
    public long SizeBytes { get; init; }
    /// <summary>获取允许访问的路径列表。</summary>
    public List<string> AllowedPaths { get; init; } = [];
    /// <summary>获取是否限制网络访问。</summary>
    public bool RestrictNetwork { get; init; }
    /// <summary>获取是否限制文件系统访问。</summary>
    public bool RestrictFileSystem { get; init; }
}