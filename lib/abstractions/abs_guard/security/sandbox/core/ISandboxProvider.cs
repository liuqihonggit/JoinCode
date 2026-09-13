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

    /// <summary>
    /// 在沙箱内执行命令 — Provider 可重写以集成进程隔离（如 JobObject）。
    /// 返回 null 表示 Provider 不支持直接执行，由 SandboxManager 回退到默认执行。
    /// </summary>
    Task<ProviderExecutionResult?> ExecuteAsync(string sandboxId, string command, string? workingDirectory, int timeoutMs, CancellationToken ct) => Task.FromResult<ProviderExecutionResult?>(null);
}
