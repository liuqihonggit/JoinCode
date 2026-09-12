namespace JoinCode.Abstractions.Security.Sandbox;

public sealed partial class SandboxDegradationResult
{
    public required SandboxType RequestedType { get; init; }
    public required SandboxType ActualType { get; init; }
    public required bool WasDegraded { get; init; }
    public SandboxInfo? Info { get; init; }
    public string? Message { get; init; }
}
