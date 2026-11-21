namespace JoinCode.Abstractions.Security.Sandbox;

/// <summary>
/// provider 返回的逐命令沙箱包装结果。
/// </summary>
public sealed partial class SandboxWrappedCommand {
    /// <summary>包装后的可执行 shell 命令串。</summary>
    public required string Command { get; init; }

    /// <summary>实际承担隔离的沙箱类型。</summary>
    public required SandboxType ProviderType { get; init; }

    /// <summary>降级/能力缺失说明（例如域名过滤不支持时 fail-closed 的原因）。</summary>
    public string? Warning { get; init; }
}
