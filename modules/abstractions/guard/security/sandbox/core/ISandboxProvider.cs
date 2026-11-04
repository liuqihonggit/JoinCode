namespace JoinCode.Abstractions.Security.Sandbox;

public interface ISandboxProvider : IAsyncDisposable {
    /// <summary>获取沙箱类型。</summary>
    SandboxType SandboxType { get; }
    /// <summary>获取是否可用。</summary>
    bool IsAvailable { get; }
    /// <summary>获取沙箱能力。</summary>
    SandboxCapabilities Capabilities { get; }
    /// <summary>
    /// 获取活跃沙箱的快照拷贝 — 用于枚举
    /// </summary>
    /// <returns>活跃沙箱数组快照</returns>
    SandboxInfo[] GetActiveSandboxes();

    /// <summary>异步创建沙箱。</summary>
    Task<SandboxInfo> CreateSandboxAsync(SandboxOptions options, CancellationToken ct = default);
    /// <summary>异步销毁沙箱。</summary>
    Task DestroySandboxAsync(string sandboxId, CancellationToken ct = default);
    /// <summary>获取沙箱信息。</summary>
    SandboxInfo? GetSandboxInfo(string sandboxId);
    /// <summary>解析沙箱内路径。</summary>
    string ResolvePath(string path, string sandboxId);
    /// <summary>异步判断路径是否在沙箱内。</summary>
    Task<bool> IsPathInSandboxAsync(string path, string sandboxId, CancellationToken ct = default);

    /// <summary>
    /// 在沙箱内执行命令 — Provider 可重写以集成进程隔离（如 JobObject）。
    /// 返回 null 表示 Provider 不支持直接执行，由 SandboxManager 回退到默认执行。
    /// </summary>
    Task<ProviderExecutionResult?> ExecuteAsync(string sandboxId, string command, string? workingDirectory, int timeoutMs, CancellationToken ct) => Task.FromResult<ProviderExecutionResult?>(null);
}