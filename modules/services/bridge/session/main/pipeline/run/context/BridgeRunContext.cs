namespace Core.Bridge;

/// <summary>
/// Bridge 运行上下文 — 承载 BridgeMain 命令的参数、可变运行状态与中间件产出
/// </summary>
public sealed class BridgeRunContext : PipelineContextBase {
    /// <summary>Bridge 主命令参数</summary>
    public required BridgeMainArgs Args { get; init; }
    /// <summary>取消令牌</summary>
    public CancellationToken CancellationToken { get; init; }

    /// <summary>早期结果（短路时设置，跳过后续管道）</summary>
    public BridgeMainResult? EarlyResult { get; set; }
    /// <summary>访问令牌</summary>
    public string? AccessToken { get; set; }
    /// <summary>基础 URL</summary>
    public string? BaseUrl { get; set; }
    /// <summary>恢复会话 ID</summary>
    public string? ResumeSessionId { get; set; }
    /// <summary>复用环境 ID</summary>
    public string? ReuseEnvironmentId { get; set; }
    /// <summary>恢复指针目录</summary>
    public string? ResumePointerDir { get; set; }
    /// <summary>是否处于恢复模式</summary>
    public bool IsResuming { get; set; }
    /// <summary>实际生效的派生模式</summary>
    public BridgeSpawnMode? EffectiveSpawnMode { get; set; }
    /// <summary>派生模式来源（默认为门控默认值）</summary>
    public BridgeSpawnModeSource SpawnModeSource { get; set; } = BridgeSpawnModeSource.GateDefault;
    /// <summary>Bridge 配置</summary>
    public BridgeConfig? Config { get; set; }
    /// <summary>初始会话 ID</summary>
    public string? InitialSessionId { get; set; }
    /// <summary>令牌刷新调度器</summary>
    public BridgeTokenRefreshScheduler? TokenRefreshScheduler { get; set; }
}