
namespace Core.Bridge;

// BridgeSessionActivity 枚举已迁移到 JoinCode.Transport.Bridge 命名空间 (Transport.Contracts)

/// <summary>
/// Bridge 显示接口 — 对齐 TS 端 BridgeLogger 的 TUI 渲染方法
/// 日志方法已迁移到 ILogger，此接口仅保留 UI 控制方法
/// </summary>
public interface IBridgeLogger
{
    /// <summary>打印启动横幅</summary>
    void PrintBanner(BridgeConfig config, string environmentId);

    /// <summary>更新空闲状态</summary>
    void UpdateIdleStatus();

    /// <summary>更新重连中状态</summary>
    void UpdateReconnectingStatus(string delayStr, string elapsedStr);

    /// <summary>更新会话状态</summary>
    void UpdateSessionStatus(string sessionId, string elapsed, BridgeSessionActivity activity, IReadOnlyList<string> trail);

    /// <summary>清除状态</summary>
    void ClearStatus();

    /// <summary>设置仓库信息</summary>
    void SetRepoInfo(string repoName, string branch);

    /// <summary>设置调试日志路径</summary>
    void SetDebugLogPath(string path);

    /// <summary>设置已附加状态</summary>
    void SetAttached(string sessionId);

    /// <summary>更新失败状态</summary>
    void UpdateFailedStatus(string error);

    /// <summary>切换 QR 码显示</summary>
    void ToggleQr();

    /// <summary>更新会话计数</summary>
    void UpdateSessionCount(int active, int max, BridgeSpawnMode mode);

    /// <summary>设置生成模式显示</summary>
    void SetSpawnModeDisplay(BridgeSpawnMode? mode);

    /// <summary>添加会话</summary>
    void AddSession(string sessionId, string url);

    /// <summary>更新会话活动</summary>
    void UpdateSessionActivity(string sessionId, BridgeSessionActivity activity);

    /// <summary>设置会话标题</summary>
    void SetSessionTitle(string sessionId, string title);

    /// <summary>移除会话</summary>
    void RemoveSession(string sessionId);

    /// <summary>刷新显示</summary>
    void RefreshDisplay();
}

/// <summary>
/// 空实现 — 非 Bridge 模式使用，避免空引用
/// </summary>
public sealed class NullBridgeLogger : IBridgeLogger
{
    public void PrintBanner(BridgeConfig config, string environmentId) { }
    public void UpdateIdleStatus() { }
    public void UpdateReconnectingStatus(string delayStr, string elapsedStr) { }
    public void UpdateSessionStatus(string sessionId, string elapsed, BridgeSessionActivity activity, IReadOnlyList<string> trail) { }
    public void ClearStatus() { }
    public void SetRepoInfo(string repoName, string branch) { }
    public void SetDebugLogPath(string path) { }
    public void SetAttached(string sessionId) { }
    public void UpdateFailedStatus(string error) { }
    public void ToggleQr() { }
    public void UpdateSessionCount(int active, int max, BridgeSpawnMode mode) { }
    public void SetSpawnModeDisplay(BridgeSpawnMode? mode) { }
    public void AddSession(string sessionId, string url) { }
    public void UpdateSessionActivity(string sessionId, BridgeSessionActivity activity) { }
    public void SetSessionTitle(string sessionId, string title) { }
    public void RemoveSession(string sessionId) { }
    public void RefreshDisplay() { }
}

/// <summary>
/// Headless 模式显示适配器 — 对齐 TS 端 createHeadlessBridgeLogger
/// TUI 渲染方法全部 noop，日志已迁移到 ILogger
/// </summary>
public sealed class HeadlessBridgeLogger : IBridgeLogger
{
    private readonly Action<string> _log;

    /// <summary>初始化 Headless 显示适配器</summary>
    /// <param name="log">输出回调 — 对齐 TS 端 opts.log</param>
    public HeadlessBridgeLogger(Action<string> log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>打印启动横幅</summary>
    public void PrintBanner(BridgeConfig config, string environmentId)
        => _log($"registered environmentId={environmentId} dir={config.Dir} spawnMode={config.SpawnMode.ToValue()} capacity={config.MaxSessions}");

    /// <summary>添加会话</summary>
    public void AddSession(string sessionId, string url)
        => _log($"session attached {sessionId}");

    /// <summary>移除会话</summary>
    public void RemoveSession(string sessionId)
        => _log($"session detached {sessionId}");

    // ===== TUI 渲染 — 全部 noop =====

    /// <summary>noop</summary>
    public void UpdateIdleStatus() { }
    /// <summary>noop</summary>
    public void UpdateReconnectingStatus(string delayStr, string elapsedStr) { }
    /// <summary>noop</summary>
    public void UpdateSessionStatus(string sessionId, string elapsed, BridgeSessionActivity activity, IReadOnlyList<string> trail) { }
    /// <summary>noop</summary>
    public void ClearStatus() { }
    /// <summary>noop</summary>
    public void SetRepoInfo(string repoName, string branch) { }
    /// <summary>noop</summary>
    public void SetDebugLogPath(string path) { }
    /// <summary>noop</summary>
    public void SetAttached(string sessionId) { }
    /// <summary>noop</summary>
    public void UpdateFailedStatus(string error) { }
    /// <summary>noop</summary>
    public void ToggleQr() { }
    /// <summary>noop</summary>
    public void UpdateSessionCount(int active, int max, BridgeSpawnMode mode) { }
    /// <summary>noop</summary>
    public void SetSpawnModeDisplay(BridgeSpawnMode? mode) { }
    /// <summary>noop</summary>
    public void UpdateSessionActivity(string sessionId, BridgeSessionActivity activity) { }
    /// <summary>noop</summary>
    public void SetSessionTitle(string sessionId, string title) { }
    /// <summary>noop</summary>
    public void RefreshDisplay() { }
}
