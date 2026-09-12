namespace JoinCode.Abstractions.Security.Sandbox;

public sealed partial class SandboxExecutionOptions
{
    public SandboxExecutionTimeout TimeoutPreset { get; init; } = SandboxExecutionTimeout.TwoMinutes;

    public int CustomTimeoutSeconds { get; init; }

    public int GetTimeoutSeconds() => TimeoutPreset switch
    {
        SandboxExecutionTimeout.TwoMinutes => 120,
        SandboxExecutionTimeout.FourMinutes => 240,
        SandboxExecutionTimeout.EightMinutes => 480,
        SandboxExecutionTimeout.Custom => CustomTimeoutSeconds > 0 ? CustomTimeoutSeconds : 120,
        _ => 120
    };
}
