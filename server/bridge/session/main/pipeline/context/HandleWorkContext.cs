namespace Core.Bridge;


/// <summary>
/// Bridge 工作管道上下文 — 承载单次 Work 处理过程中的共享状态与回调
/// </summary>
public sealed class HandleWorkContext : IPipelineContext
{
    /// <summary>Bridge 配置（必需）</summary>
    public required BridgeConfig Config { get; init; }
    /// <summary>当前处理的 Bridge 工作项（必需）</summary>
    public required BridgeWorkItem Work { get; init; }
    /// <summary>取消令牌</summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>解码后的工作密钥</summary>
    public BridgeWorkSecret? Secret { get; set; }
    /// <summary>会话入口令牌</summary>
    public string? SessionIngressToken { get; set; }
    /// <summary>密钥中携带的 API 基础 URL</summary>
    public string? SecretApiBaseUrl { get; set; }
    /// <summary>是否使用 CCR v2 模式</summary>
    public bool UseCcrV2 { get; set; }
    /// <summary>Worker epoch（v2 模式）</summary>
    public int? WorkerEpoch { get; set; }
    /// <summary>SDK URL（WebSocket/SSE 端点）</summary>
    public string? SdkUrl { get; set; }
    /// <summary>已创建的 worktree 路径</summary>
    public string? CreatedWorktreePath { get; set; }
    /// <summary>子进程句柄</summary>
    public BridgeSubprocessHandle? Handle { get; set; }

    /// <summary>是否短路管道（跳过后续中间件）</summary>
    public bool ShortCircuited { get; set; }

    /// <summary>环境 ID</summary>
    public string? EnvironmentId { get; set; }

    /// <summary>子进程启动目录</summary>
    public string? SpawnDir { get; set; }
    /// <summary>访问令牌获取委托</summary>
    public Func<string?>? GetAccessToken { get; set; }
    /// <summary>权限模式</summary>
    public string? PermissionMode { get; set; }
    /// <summary>权限请求回调（请求, 令牌）</summary>
    public Action<BridgePermissionRequest, string?>? OnPermissionRequest { get; set; }
    /// <summary>活动回调</summary>
    public Action<BridgeNdjsonActivity>? OnActivity { get; set; }
    /// <summary>首条用户消息回调</summary>
    public Action<string>? OnFirstUserMessage { get; set; }

    /// <summary>子进程生成器</summary>
    internal BridgeSubprocessSpawner? Spawner { get; set; }
    /// <summary>轮询配置</summary>
    internal BridgeMainPollConfig? PollConfig { get; set; }

    /// <summary>活跃会话句柄表（会话 ID → 句柄）</summary>
    internal ConcurrentDictionary<string, BridgeSubprocessHandle> ActiveSessions { get; set; } = new();
    /// <summary>会话启动时间表（会话 ID → 启动时间）</summary>
    internal ConcurrentDictionary<string, DateTime> SessionStartTimes { get; set; } = new();
    /// <summary>会话与工作 ID 映射（会话 ID → Work ID）</summary>
    internal ConcurrentDictionary<string, string> SessionWorkIds { get; set; } = new();
    /// <summary>会话入口令牌表（会话 ID → 令牌）</summary>
    internal ConcurrentDictionary<string, string> SessionIngressTokens { get; set; } = new();
    /// <summary>会话 worktree 路径表（会话 ID → worktree 路径）</summary>
    internal ConcurrentDictionary<string, string> SessionWorktrees { get; set; } = new();
    /// <summary>已完成工作 ID 集合</summary>
    internal ConcurrentDictionary<string, byte> CompletedWorkIds { get; set; } = new();
    /// <summary>v2 会话集合</summary>
    internal ConcurrentDictionary<string, byte> V2Sessions { get; set; } = new();
    /// <summary>会话兼容 ID 映射</summary>
    internal ConcurrentDictionary<string, string> SessionCompatIds { get; set; } = new();

    /// <summary>停止工作委托（Work ID, 取消令牌）</summary>
    internal Func<string, CancellationToken, Task>? StopWorkAsync { get; set; }
    /// <summary>清理任务跟踪委托</summary>
    internal Action<Task>? TrackCleanup { get; set; }
    /// <summary>容量唤醒回调</summary>
    internal Action? CapacityWake { get; set; }
    /// <summary>遥测计数回调（指标名, 标签字典）</summary>
    internal Action<string, Dictionary<string, string>?>? TelemetryCount { get; set; }

    /// <summary>管道是否失败</summary>
    bool IPipelineContext.Failed { get; set; }
    /// <summary>管道失败错误消息</summary>
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

    /// <summary>
    /// 标记 Work 失败：记录完成、停止工作、唤醒容量、短路管道
    /// </summary>
    /// <param name="ct">取消令牌</param>
    internal void FailWork(CancellationToken ct = default)
    {
        CompletedWorkIds.TryAdd(Work.WorkId, 0);
        if (TrackCleanup is not null && StopWorkAsync is not null)
        {
            TrackCleanup(StopWorkAsync(Work.WorkId, ct));
        }
        CapacityWake?.Invoke();
        ShortCircuited = true;
    }
}
