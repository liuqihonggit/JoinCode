namespace Core.Bridge;


/// <summary>
/// 关闭管道上下文 — 承载 Bridge 关闭流程的所有共享状态
/// </summary>
public sealed class ShutdownContext : IPipelineContext
{
    /// <summary>是否处于恢复（resume）模式</summary>
    public bool IsResuming { get; init; }

    /// <summary>是否为致命退出（fatal exit）</summary>
    public bool FatalExit { get; init; }

    /// <summary>环境 ID</summary>
    public string? EnvironmentId { get; init; }

    /// <summary>子进程派生模式</summary>
    public BridgeSpawnMode SpawnMode { get; init; }

    /// <summary>恢复指针目录路径</summary>
    public string? ResumePointerDir { get; init; }

    /// <summary>会话跟踪器 — 聚合 Sessions/WorkCompletion/Titles</summary>
    internal BridgeSessionTracker Tracker { get; set; } = new();

    /// <summary>子进程派生器</summary>
    internal BridgeSubprocessSpawner? Spawner { get; set; }

    /// <summary>Bridge API 客户端</summary>
    internal BridgeApiClient? ApiClient { get; set; }

    /// <summary>崩溃恢复指针服务</summary>
    internal BridgePointerService? PointerService { get; set; }

    /// <summary>工作目录路径</summary>
    internal string? WorkingDirectory { get; set; }

    /// <summary>归档会话异步委托（会话 ID → 任务）</summary>
    internal Func<string, CancellationToken, Task>? ArchiveSession { get; set; }

    /// <summary>注销键盘监听器回调</summary>
    internal Action? UnregisterKeyboardListener { get; set; }

    /// <summary>轮询循环的取消令牌源</summary>
    internal CancellationTokenSource? LoopCts { get; set; }

    /// <summary>轮询循环任务</summary>
    internal Task? LoopTask { get; set; }

    /// <summary>指针刷新定时器</summary>
    internal Timer? PointerRefreshTimer { get; set; }

    /// <summary>管道是否失败</summary>
    bool IPipelineContext.Failed { get; set; }

    /// <summary>管道错误消息</summary>
    string? IPipelineContext.ErrorMessage { get; set; }

    /// <summary>
    /// 标记管道失败并记录错误消息
    /// </summary>
    /// <param name="message">错误消息</param>
    void IPipelineContext.Fail(string message)
    {
        ((IPipelineContext)this).Failed = true;
        ((IPipelineContext)this).ErrorMessage = message;
    }
}
