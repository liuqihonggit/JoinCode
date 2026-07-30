namespace JoinCode.Abstractions.Security.Sandbox;

public sealed partial class SandboxInfo
{
    public required SandboxType Type { get; init; }
    public required string SandboxId { get; init; }
    public required string RootPath { get; init; }
    public required DateTime EnteredAt { get; init; }
    public required bool IsRestricted { get; init; }
    public SandboxCapabilities Capabilities { get; init; }
    public long SizeBytes { get; init; }
    public List<string>? AllowedPaths { get; init; }
    public bool RestrictNetwork { get; init; }
    public bool RestrictFileSystem { get; init; }
}
