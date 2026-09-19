namespace JoinCode.Abstractions.Configuration.Execution;

/// <summary>
/// 子代理卡死防护配置 — 纵深防御四层参数（ADR 0106）。
/// L1 预防(工具超时) + L2 检测(无输出+巡查+链路) + L3 干预(激活+抢塞) + L4 恢复(渐进式压缩)。
/// </summary>
public sealed class SubAgentLivenessOptions {
    /// <summary>
    /// L1 预防：子代理执行超时（秒），0=不限。默认 300（5分钟）。
    /// 消费方：AgentLifecycleManager.ExecuteAsync 通过 TimeoutHelper.CreateLinkedTimeout 施加。
    /// </summary>
    public int AgentTimeoutSeconds { get; set; } = WorkflowConstants.Timeouts.AgentTimeoutSeconds;

    /// <summary>
    /// L2 检测：完全无输出超时阈值（秒）。子代理 LastActivityAt 超过此值且无孙代理 → 疑似卡死。
    /// 默认 30。
    /// </summary>
    public int IdleThresholdSeconds { get; set; } = 30;

    /// <summary>
    /// L2 检测：80% 巡查触发阈值。完成比例达到此值时触发一次全量巡查。默认 0.8。
    /// </summary>
    public double CompletionCheckThreshold { get; set; } = 0.8;

    /// <summary>
    /// L2 检测：二次确认时间窗口（秒）。Suspected 状态下在此窗口内再次 Idle → Confirmed。默认 5。
    /// </summary>
    public int ConfirmationWindowSeconds { get; set; } = 5;

    /// <summary>
    /// L2 检测：定时扫描间隔（秒）。后台扫描器每隔此值扫描所有 Running 子代理活性。默认 10。
    /// </summary>
    public int ScanIntervalSeconds { get; set; } = 10;

    /// <summary>
    /// L2 检测：链路卡死节点数阈值。子孙链上 Confirmed 节点数达到此值 → 整链告警。默认 3。
    /// </summary>
    public int ChainStallThreshold { get; set; } = 3;

    /// <summary>
    /// L3 干预：代理池最大保留数。子代理完成后回池，池满则直接 Dispose。默认 8。
    /// </summary>
    public int PoolMaxSize { get; set; } = 8;

    /// <summary>
    /// L3 干预：代理池空闲超时（秒）。回池后超过此值未被抢塞 → 真正 Dispose。默认 300。
    /// </summary>
    public int PoolIdleTimeoutSeconds { get; set; } = 300;

    /// <summary>
    /// L3 干预：抢塞新任务时上下文窗口剩余比例阈值，低于此值拒绝抢塞先压缩。默认 0.2。
    /// </summary>
    public double PreemptMinWindowRatio { get; set; } = 0.2;

    /// <summary>
    /// L3 干预：激活动作后等待恢复的超时（秒）。超时后触发 L4 压缩。默认 10。
    /// </summary>
    public int ActivationRecoverySeconds { get; set; } = 10;

    /// <summary>
    /// 校验配置合法性 — 配置加载时调用，非法值抛 ArgumentException。
    /// </summary>
    public void Validate() {
        if (AgentTimeoutSeconds < 0)
            throw new ArgumentException("AgentTimeoutSeconds 必须 >= 0（0=不限）", nameof(AgentTimeoutSeconds));
        if (IdleThresholdSeconds < 1)
            throw new ArgumentException("IdleThresholdSeconds 必须 >= 1", nameof(IdleThresholdSeconds));
        if (CompletionCheckThreshold is < 0 or > 1)
            throw new ArgumentException("CompletionCheckThreshold 必须在 [0, 1] 范围", nameof(CompletionCheckThreshold));
        if (ConfirmationWindowSeconds < 1)
            throw new ArgumentException("ConfirmationWindowSeconds 必须 >= 1", nameof(ConfirmationWindowSeconds));
        if (ScanIntervalSeconds < 1)
            throw new ArgumentException("ScanIntervalSeconds 必须 >= 1", nameof(ScanIntervalSeconds));
        if (ChainStallThreshold < 1)
            throw new ArgumentException("ChainStallThreshold 必须 >= 1", nameof(ChainStallThreshold));
        if (PoolMaxSize < 0)
            throw new ArgumentException("PoolMaxSize 必须 >= 0（0=禁用代理池）", nameof(PoolMaxSize));
        if (PoolIdleTimeoutSeconds < 1)
            throw new ArgumentException("PoolIdleTimeoutSeconds 必须 >= 1", nameof(PoolIdleTimeoutSeconds));
        if (PreemptMinWindowRatio is < 0 or > 1)
            throw new ArgumentException("PreemptMinWindowRatio 必须在 [0, 1] 范围", nameof(PreemptMinWindowRatio));
        if (ActivationRecoverySeconds < 1)
            throw new ArgumentException("ActivationRecoverySeconds 必须 >= 1", nameof(ActivationRecoverySeconds));
    }
}