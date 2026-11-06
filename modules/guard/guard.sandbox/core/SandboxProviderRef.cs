namespace Core.Security.Sandbox;

/// <summary>
/// 沙箱提供器借用句柄 — 仅转发调用方使用的业务成员,不实现 IAsyncDisposable/IDisposable。
/// 容器(SandboxManager)持有提供器所有权,调用方仅借用,不应释放。
/// 方案 D:借用源返回非 IDisposable 句柄,消除 JCC9305 误报。
/// </summary>
internal readonly struct SandboxProviderRef {
    private readonly ISandboxProvider _provider;

    internal SandboxProviderRef(ISandboxProvider provider) => _provider = provider;

    /// <summary>获取底层提供器引用 — 仅供同程序集方法(如 SetSandboxToProvider)传递所有权持有者使用。</summary>
    internal ISandboxProvider Provider => _provider;

    /// <summary>获取沙箱类型。</summary>
    public SandboxType SandboxType => _provider.SandboxType;
    /// <summary>获取是否可用。</summary>
    public bool IsAvailable => _provider.IsAvailable;
    /// <summary>获取沙箱能力。</summary>
    public SandboxCapabilities Capabilities => _provider.Capabilities;
    /// <summary>获取活跃沙箱的快照拷贝 — 用于枚举。</summary>
    public SandboxInfo[] GetActiveSandboxes() => _provider.GetActiveSandboxes();
    /// <summary>异步创建沙箱。</summary>
    public Task<SandboxInfo> CreateSandboxAsync(SandboxOptions options, CancellationToken ct = default) => _provider.CreateSandboxAsync(options, ct);
    /// <summary>异步销毁沙箱。</summary>
    public Task DestroySandboxAsync(string sandboxId, CancellationToken ct = default) => _provider.DestroySandboxAsync(sandboxId, ct);
    /// <summary>获取沙箱信息。</summary>
    public SandboxInfo? GetSandboxInfo(string sandboxId) => _provider.GetSandboxInfo(sandboxId);
    /// <summary>解析沙箱内路径。</summary>
    public string ResolvePath(string path, string sandboxId) => _provider.ResolvePath(path, sandboxId);
    /// <summary>异步判断路径是否在沙箱内。</summary>
    public Task<bool> IsPathInSandboxAsync(string path, string sandboxId, CancellationToken ct = default) => _provider.IsPathInSandboxAsync(path, sandboxId, ct);
    /// <summary>在沙箱内执行命令 — 转发到底层提供器。</summary>
    public Task<ProviderExecutionResult?> ExecuteAsync(string sandboxId, string command, string? workingDirectory, int timeoutMs, CancellationToken ct) => _provider.ExecuteAsync(sandboxId, command, workingDirectory, timeoutMs, ct);
}
