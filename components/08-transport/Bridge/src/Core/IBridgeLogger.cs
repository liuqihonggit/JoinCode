
namespace Core.Bridge;

// BridgeSessionActivity 枚举已迁移到 JoinCode.Transport.Bridge 命名空间 (Transport.Contracts)

/// <summary>
/// Bridge 日志接口 — 对齐 TS 端 BridgeLogger (20+ 方法)
/// 用于 Bridge 模式下的状态显示和日志记录
/// </summary>
public interface IBridgeLogger
{
    /// <summary>打印启动横幅</summary>
    void PrintBanner(BridgeConfig config, string environmentId);

    /// <summary>记录会话开始</summary>
    void LogSessionStart(string sessionId, string prompt);

    /// <summary>记录会话完成</summary>
    void LogSessionComplete(string sessionId, long durationMs);

    /// <summary>记录会话失败</summary>
    void LogSessionFailed(string sessionId, string error);

    /// <summary>记录状态消息</summary>
    void LogStatus(string message);

    /// <summary>记录详细日志</summary>
    void LogVerbose(string message);

    /// <summary>记录错误</summary>
    void LogError(string message);

    /// <summary>记录重连成功</summary>
    void LogReconnected(long disconnectedMs);

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
    public void LogSessionStart(string sessionId, string prompt) { }
    public void LogSessionComplete(string sessionId, long durationMs) { }
    public void LogSessionFailed(string sessionId, string error) { }
    public void LogStatus(string message) { }
    public void LogVerbose(string message) { }
    public void LogError(string message) { }
    public void LogReconnected(long disconnectedMs) { }
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
/// Headless 模式日志适配器 — 对齐 TS 端 createHeadlessBridgeLogger
/// 业务事件路由到 log 回调，TUI 渲染方法全部 noop
/// </summary>
public sealed class HeadlessBridgeLogger : IBridgeLogger
{
    private readonly Action<string> _log;

    /// <summary>初始化 Headless 日志适配器</summary>
    /// <param name="log">日志输出回调 — 对齐 TS 端 opts.log</param>
    public HeadlessBridgeLogger(Action<string> log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    // ===== 业务事件 — 路由到 log 回调 =====

    /// <summary>打印启动横幅 — 对齐 TS 端 printBanner</summary>
    public void PrintBanner(BridgeConfig config, string environmentId)
        => _log($"registered environmentId={environmentId} dir={config.Dir} spawnMode={config.SpawnMode.ToValue()} capacity={config.MaxSessions}");

    /// <summary>记录会话开始</summary>
    public void LogSessionStart(string sessionId, string prompt)
        => _log($"session start {sessionId}");

    /// <summary>记录会话完成</summary>
    public void LogSessionComplete(string sessionId, long durationMs)
        => _log($"session complete {sessionId} ({durationMs}ms)");

    /// <summary>记录会话失败</summary>
    public void LogSessionFailed(string sessionId, string error)
        => _log($"session failed {sessionId}: {error}");

    /// <summary>记录状态消息</summary>
    public void LogStatus(string message) => _log(message);

    /// <summary>记录详细日志</summary>
    public void LogVerbose(string message) => _log(message);

    /// <summary>记录错误 — 对齐 TS 端 logError 带 error: 前缀</summary>
    public void LogError(string message) => _log($"error: {message}");

    /// <summary>记录重连成功</summary>
    public void LogReconnected(long disconnectedMs)
        => _log($"reconnected after {disconnectedMs}ms");

    /// <summary>添加会话 — 对齐 TS 端 addSession（忽略 url）</summary>
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
