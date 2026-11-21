namespace JoinCode.Abstractions.Security.Sandbox;

public interface ISandboxProvider : IAsyncDisposable {
    /// <summary>获取沙箱类型。</summary>
    SandboxType SandboxType { get; }
    /// <summary>获取是否可用。</summary>
    bool IsAvailable { get; }
    /// <summary>获取沙箱能力。</summary>
    SandboxCapabilities Capabilities { get; }

    /// <summary>是否支持逐命令包装（bwrap 等轻量 provider 支持；会话型 provider 返回 false）。</summary>
    bool SupportsCommandWrapping => false;
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

    /// <summary>
    /// 逐命令包装 — 返回可直接交给宿主 shell 执行的包装命令串（如 <c>bwrap ... -- sh -c ...</c>）。
    /// 返回 false 表示该 provider 不支持逐命令包装（由门控回落为不沙箱并给出警告）。
    /// </summary>
    /// <param name="request">包装请求（命令、工作目录、文件系统/网络清单）。</param>
    /// <param name="wrapped">包装结果。</param>
    /// <returns>支持并完成包装返回 true。</returns>
    bool TryWrapCommand(SandboxWrapRequest request, [NotNullWhen(true)] out SandboxWrappedCommand? wrapped) {
        wrapped = null;
        return false;
    }
}