namespace JoinCode.Abstractions.Security.Sandbox;

public sealed partial class SandboxExecutionOptions {
    /// <summary>获取或设置超时预设。</summary>
    public SandboxExecutionTimeout TimeoutPreset { get; init; } = SandboxExecutionTimeout.TwoMinutes;

    /// <summary>获取或设置自定义超时秒数。</summary>
    public int CustomTimeoutSeconds { get; init; }

    /// <summary>获取超时秒数。</summary>
    public int GetTimeoutSeconds() => TimeoutPreset switch {
        SandboxExecutionTimeout.TwoMinutes => 120,
        SandboxExecutionTimeout.FourMinutes => 240,
        SandboxExecutionTimeout.EightMinutes => 480,
        SandboxExecutionTimeout.Custom => CustomTimeoutSeconds > 0 ? CustomTimeoutSeconds : 120,
        _ => 120
    };
}