namespace JoinCode.Abstractions.Security.Sandbox;

public interface ISandboxManager : IDisposable
{
    ISandboxProvider? ActiveProvider { get; }
    SandboxType ActiveSandboxType { get; }
    bool IsInSandbox { get; }
    SandboxInfo? CurrentSandbox { get; }
    string? CurrentSandboxId { get; }
    SandboxHealthState HealthState { get; }
    IReadOnlyList<SandboxType> AvailableTypes { get; }

    Task<SandboxInfo> EnterSandboxAsync(SandboxOptions options, CancellationToken ct = default);
    Task ExitSandboxAsync(CancellationToken ct = default);
    Task SwitchProviderAsync(SandboxType type, CancellationToken ct = default);
    ISandboxProvider? GetProvider(SandboxType type);
    string ResolvePath(string path);

    Task<SandboxInfo> CreateSandboxAsync(SandboxType type, SandboxOptions options, CancellationToken ct = default);
    Task DestroySandboxAsync(string sandboxId, CancellationToken ct = default);
    SandboxInfo? GetSandboxInfo(string sandboxId);
    string ResolvePath(string path, string sandboxId);

    Task<SandboxDegradationResult> TryEnterWithFallbackAsync(SandboxOptions options, CancellationToken ct = default);
    Task<SandboxExecutionResult> ExecuteInSandboxAsync(string command, SandboxExecutionOptions options, CancellationToken ct = default);
    Task<SandboxExecutionResult> ContinueExecutionAsync(string executionId, string action, CancellationToken ct = default);
}
