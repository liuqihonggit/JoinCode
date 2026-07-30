namespace JoinCode.Abstractions.Security.Sandbox;

public sealed partial class SandboxOptions
{
    public required SandboxType Type { get; init; }
    public string? SandboxRoot { get; init; }
    public bool RestrictNetwork { get; init; } = true;
    public bool RestrictFileSystem { get; init; } = true;
    public List<string>? AllowedPaths { get; init; }
    public int MemoryLimitMb { get; init; }
    public int CpuLimitPercent { get; init; }
    public int TimeLimitSeconds { get; init; }
    public string? DockerImage { get; init; }
    public Dictionary<string, string>? EnvironmentOverrides { get; init; }
}
