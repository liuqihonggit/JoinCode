namespace JoinCode.Abstractions.Security;

public interface IScratchpadSandbox : IAsyncDisposable
{
    Task<string> CreateSandboxAsync(string? basePath = null, CancellationToken ct = default);
    Task<bool> IsPathInSandboxAsync(string path, string sandboxId, CancellationToken ct = default);
    Task<string> ResolveSandboxPathAsync(string path, string sandboxId, CancellationToken ct = default);
    Task DestroySandboxAsync(string sandboxId, CancellationToken ct = default);
    SandboxInfo GetSandboxInfo(string sandboxId);
}

public sealed partial class SandboxInfo
{
    public required string SandboxId { get; init; }
    public required string RootPath { get; init; }
    public required DateTime CreatedAt { get; init; }
    public long SizeBytes { get; init; }
}
