namespace JoinCode.Abstractions.Security.Sandbox;

public sealed partial class SandboxSwitchResult
{
    public required SandboxType FromType { get; init; }
    public required SandboxType ToType { get; init; }
    public required bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public SandboxInfo? NewSandboxInfo { get; init; }
}
