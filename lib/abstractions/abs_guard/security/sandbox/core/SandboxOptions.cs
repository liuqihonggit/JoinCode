namespace JoinCode.Abstractions.Security.Sandbox;

public sealed partial class SandboxOptions {
    /// <summary>获取沙箱类型。</summary>
    public required SandboxType Type { get; init; }
    /// <summary>获取沙箱根目录。</summary>
    public string? SandboxRoot { get; init; }
    /// <summary>获取是否限制网络。</summary>
    public bool RestrictNetwork { get; init; } = true;
    /// <summary>获取是否限制文件系统。</summary>
    public bool RestrictFileSystem { get; init; } = true;
    /// <summary>获取允许的路径列表。</summary>
    public List<string> AllowedPaths { get; init; } = [];
    /// <summary>获取内存上限(MB)。</summary>
    public int MemoryLimitMb { get; init; }
    /// <summary>获取 CPU 限制百分比。</summary>
    public int CpuLimitPercent { get; init; }
    /// <summary>获取时间限制(秒)。</summary>
    public int TimeLimitSeconds { get; init; }
    /// <summary>获取 Docker 镜像。</summary>
    public string? DockerImage { get; init; }
    /// <summary>获取环境变量覆盖字典。</summary>
    public Dictionary<string, string> EnvironmentOverrides { get; init; } = [];
}