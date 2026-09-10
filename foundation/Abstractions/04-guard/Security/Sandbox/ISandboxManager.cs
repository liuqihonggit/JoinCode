namespace JoinCode.Abstractions.Security.Sandbox;

public interface ISandboxManager : IDisposable
{
    ISandboxProvider? ActiveProvider { get; }
    SandboxType ActiveSandboxType { get; }
    bool IsInSandbox { get; }
    SandboxInfo? CurrentSandbox { get; }
    string? CurrentSandboxId { get; }
    SandboxHealthState HealthState { get; }
    IEnumerable<SandboxType> AvailableTypes { get; }

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

    /// <summary>运行时添加沙箱提供器 — 插件加载时调用(ADR 0098)</summary>
    bool AddProvider(ISandboxProvider provider);

    /// <summary>运行时移除沙箱提供器 — 插件卸载时调用</summary>
    bool RemoveProvider(SandboxType type);
}
