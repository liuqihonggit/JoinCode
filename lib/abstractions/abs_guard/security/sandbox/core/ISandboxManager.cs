namespace JoinCode.Abstractions.Security.Sandbox;

public interface ISandboxManager : IDisposable {
    /// <summary>获取当前活跃的沙箱提供器。</summary>
    ISandboxProvider? ActiveProvider { get; }
    /// <summary>获取活跃沙箱类型。</summary>
    SandboxType ActiveSandboxType { get; }
    /// <summary>获取是否处于沙箱内。</summary>
    bool IsInSandbox { get; }
    /// <summary>获取当前沙箱信息。</summary>
    SandboxInfo? CurrentSandbox { get; }
    /// <summary>获取当前沙箱标识。</summary>
    string? CurrentSandboxId { get; }
    /// <summary>获取健康状态。</summary>
    SandboxHealthState HealthState { get; }
    /// <summary>获取可用沙箱类型集合。</summary>
    IEnumerable<SandboxType> AvailableTypes { get; }

    /// <summary>进入沙箱。</summary>
    /// <param name="options">沙箱选项。</param>
    /// <param name="ct">取消令牌。</param>
    Task<SandboxInfo> EnterSandboxAsync(SandboxOptions options, CancellationToken ct = default);
    /// <summary>退出当前沙箱。</summary>
    /// <param name="ct">取消令牌。</param>
    Task ExitSandboxAsync(CancellationToken ct = default);
    /// <summary>切换沙箱提供器。</summary>
    /// <param name="type">目标沙箱类型。</param>
    /// <param name="ct">取消令牌。</param>
    Task SwitchProviderAsync(SandboxType type, CancellationToken ct = default);
    /// <summary>获取指定类型的提供器。</summary>
    /// <param name="type">沙箱类型。</param>
    ISandboxProvider? GetProvider(SandboxType type);
    /// <summary>将路径解析为沙箱内路径。</summary>
    /// <param name="path">原始路径。</param>
    string ResolvePath(string path);

    /// <summary>创建新沙箱。</summary>
    /// <param name="type">沙箱类型。</param>
    /// <param name="options">沙箱选项。</param>
    /// <param name="ct">取消令牌。</param>
    Task<SandboxInfo> CreateSandboxAsync(SandboxType type, SandboxOptions options, CancellationToken ct = default);
    /// <summary>销毁指定沙箱。</summary>
    /// <param name="sandboxId">沙箱标识。</param>
    /// <param name="ct">取消令牌。</param>
    Task DestroySandboxAsync(string sandboxId, CancellationToken ct = default);
    /// <summary>获取指定沙箱信息。</summary>
    /// <param name="sandboxId">沙箱标识。</param>
    SandboxInfo? GetSandboxInfo(string sandboxId);
    /// <summary>将路径解析为指定沙箱内路径。</summary>
    /// <param name="path">原始路径。</param>
    /// <param name="sandboxId">沙箱标识。</param>
    string ResolvePath(string path, string sandboxId);

    /// <summary>尝试进入沙箱,失败时回退。</summary>
    /// <param name="options">沙箱选项。</param>
    /// <param name="ct">取消令牌。</param>
    Task<SandboxDegradationResult> TryEnterWithFallbackAsync(SandboxOptions options, CancellationToken ct = default);
    /// <summary>在沙箱中执行命令。</summary>
    /// <param name="command">要执行的命令。</param>
    /// <param name="options">执行选项。</param>
    /// <param name="ct">取消令牌。</param>
    Task<SandboxExecutionResult> ExecuteInSandboxAsync(string command, SandboxExecutionOptions options, CancellationToken ct = default);
    /// <summary>继续执行挂起的沙箱执行。</summary>
    /// <param name="executionId">执行标识。</param>
    /// <param name="action">执行动作。</param>
    /// <param name="ct">取消令牌。</param>
    Task<SandboxExecutionResult> ContinueExecutionAsync(string executionId, string action, CancellationToken ct = default);

    /// <summary>运行时添加沙箱提供器 — 插件加载时调用(ADR 0098)</summary>
    bool AddProvider(ISandboxProvider provider);

    /// <summary>运行时移除沙箱提供器 — 插件卸载时调用</summary>
    bool RemoveProvider(SandboxType type);
}