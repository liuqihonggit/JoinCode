namespace JoinCode.Abstractions.Security.Sandbox;

public interface ISandboxProvider : IAsyncDisposable
{
    SandboxType SandboxType { get; }
    bool IsAvailable { get; }
    SandboxCapabilities Capabilities { get; }
    IReadOnlyCollection<SandboxInfo> ActiveSandboxes { get; }

    Task<SandboxInfo> CreateSandboxAsync(SandboxOptions options, CancellationToken ct = default);
    Task DestroySandboxAsync(string sandboxId, CancellationToken ct = default);
    SandboxInfo? GetSandboxInfo(string sandboxId);
    string ResolvePath(string path, string sandboxId);
    Task<bool> IsPathInSandboxAsync(string path, string sandboxId, CancellationToken ct = default);
}
