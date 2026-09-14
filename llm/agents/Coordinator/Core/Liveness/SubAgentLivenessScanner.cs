namespace Core.Agents.Coordinator.Liveness;

/// <summary>
/// 子代理卡死事件参数 — 检测器确认卡死时触发
/// </summary>
public sealed class SubAgentStalledEventArgs : EventArgs
{
    /// <summary>卡死的子代理 ID</summary>
    public required string AgentId { get; init; }

    /// <summary>卡死类型</summary>
    public required StallKind Kind { get; init; }

    /// <summary>最后活跃时刻</summary>
    public DateTimeOffset LastActivityAt { get; init; }

    /// <summary>检测结果</summary>
    public required SubAgentIdleResult Result { get; init; }
}

/// <summary>
/// 卡死类型 — 区分单点卡死和链路卡死
/// </summary>
public enum StallKind
{
    /// <summary>单点卡死 — 单个子代理无输出超时</summary>
    Single,

    /// <summary>链路卡死 — 子孙链所有节点都卡死</summary>
    Chain,
}

/// <summary>
/// 子代理活性扫描器 — 后台定时扫描 + 80% 里程碑巡查（ADR 0106 L2 检测层）
/// <para>
/// 职责：
/// 1. 订阅 <see cref="AgentStateMachine.StateChanged"/> — 完成率≥80%时触发全量巡查
/// 2. 定时扫描（默认10s）所有 Running 子代理的 LastActivityAt
/// 3. 对每个子代理调用 <see cref="SubAgentIdleDetector.Record"/> 驱动状态机
/// 4. Confirmed 时触发 <see cref="AgentStalled"/> 事件，由集成层处理 L3 激活
/// </para>
/// </summary>
public sealed partial class SubAgentLivenessScanner : IAsyncDisposable
{
    private readonly AgentStateMachine _stateMachine;
    private readonly IAgentLifecycleManager _lifecycleManager;
    private readonly IForkSubAgentManager _forkManager;
    private readonly SubAgentLivenessOptions _options;
    private readonly ILogger? _logger;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ConcurrentDictionary<string, SubAgentIdleDetector> _detectors = new();
    private readonly SubAgentChainStallDetector _chainDetector;
    private readonly CancellationTokenSource _cts = new();
    private readonly PeriodicTimer? _scanTimer;
    private volatile bool _milestoneScanTriggered;
    private Task? _scanLoop;

    /// <summary>子代理卡死事件 — 检测器确认卡死时触发，由集成层订阅处理 L3 激活</summary>
    public event EventHandler<SubAgentStalledEventArgs>? AgentStalled;

    /// <summary>链路卡死事件 — 整链卡死时触发，由集成层订阅处理 L4 压缩</summary>
    public event EventHandler<ChainStallResult>? ChainStalled;

    /// <summary>
    /// 构造子代理活性扫描器
    /// </summary>
    public SubAgentLivenessScanner(
        AgentStateMachine stateMachine,
        IAgentLifecycleManager lifecycleManager,
        IForkSubAgentManager forkManager,
        SubAgentLivenessOptions options,
        ILogger? logger = null,
        Func<DateTimeOffset>? clock = null)
    {
        _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        _lifecycleManager = lifecycleManager ?? throw new ArgumentNullException(nameof(lifecycleManager));
        _forkManager = forkManager ?? throw new ArgumentNullException(nameof(forkManager));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
        _chainDetector = new SubAgentChainStallDetector(_options.ChainStallThreshold);
        _scanTimer = new PeriodicTimer(TimeSpan.FromSeconds(_options.ScanIntervalSeconds));
    }

    /// <summary>
    /// 启动扫描器 — 订阅状态变更事件 + 启动定时扫描循环
    /// </summary>
    public void Start()
    {
        _stateMachine.StateChanged += OnStateChanged;
        _scanLoop = Task.Run(ScanLoopAsync);
        _logger?.LogInformation("[SubAgentLivenessScanner] 启动，扫描间隔: {Interval}s，空闲阈值: {Idle}s",
            _options.ScanIntervalSeconds, _options.IdleThresholdSeconds);
    }

    /// <summary>
    /// 状态变更回调 — 检查完成率，80%时触发全量巡查；终态时清理检测器
    /// </summary>
    private void OnStateChanged(object? sender, AgentStateChangedEventArgs e)
    {
        // 终态时清理检测器
        if (e.NewState.IsTerminal())
        {
            _detectors.TryRemove(e.AgentId, out _);
            return;
        }

        // 80% 里程碑巡查
        if (_milestoneScanTriggered) return;
        var report = _stateMachine.GetReport();
        if (report.TotalAgents == 0) return;

        var completionRate = (double)report.CompletedCount / report.TotalAgents;
        if (completionRate >= _options.CompletionCheckThreshold)
        {
            _milestoneScanTriggered = true;
            _logger?.LogInformation("[SubAgentLivenessScanner] 完成率 {Rate:P0} ≥ {Threshold:P0}，触发全量巡查",
                completionRate, _options.CompletionCheckThreshold);
            _ = Task.Run(() => ScanAllAsync(_cts.Token));
        }
    }

    /// <summary>
    /// 定时扫描循环
    /// </summary>
    private async Task ScanLoopAsync()
    {
        try
        {
            while (await _scanTimer!.WaitForNextTickAsync(_cts.Token).ConfigureAwait(false))
            {
                await ScanAllAsync(_cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[SubAgentLivenessScanner] 扫描循环异常");
        }
    }

    /// <summary>
    /// 扫描所有 Running 子代理的活性
    /// </summary>
    public async Task ScanAllAsync(CancellationToken ct = default)
    {
        var agents = await _lifecycleManager.GetAllAgentsAsync(ct).ConfigureAwait(false);
        var runningAgents = agents
            .Where(a => a.Status == TaskExecutionStatus.Running)
            .ToList();

        if (runningAgents.Count == 0) return;

        // 获取活跃 fork，构建 ParentSessionId 集合（有活跃孙代理的父会话）
        var activeForks = await _forkManager.GetActiveForksAsync(ct).ConfigureAwait(false);
        var sessionsWithActiveChildren = activeForks
            .Where(f => f.State == ForkState.Running)
            .Select(f => f.ParentSessionId)
            .ToFrozenSet();

        var confirmedIds = new HashSet<string>();
        var parentMap = new Dictionary<string, string>();

        foreach (var agent in runningAgents)
        {
            var agentId = agent.ObjectId.UniqueId;
            var lastActivity = GetLastActivityAt(agent);
            var sessionId = GetSessionId(agent);
            var hasGrandchildren = sessionsWithActiveChildren.Contains(sessionId);

            var detector = _detectors.GetOrAdd(agentId, _ => new SubAgentIdleDetector(
                TimeSpan.FromSeconds(_options.IdleThresholdSeconds),
                TimeSpan.FromSeconds(_options.ConfirmationWindowSeconds),
                _clock));

            var result = detector.Record(lastActivity, hasGrandchildren);

            // 构建 parentMap（用于链路检测）
            var parentId = GetParentAgentId(agent);
            if (parentId is not null)
                parentMap[agentId] = parentId;

            if (result.IsStalled)
            {
                confirmedIds.Add(agentId);
                _logger?.LogWarning("[SubAgentLivenessScanner] 子代理 {AgentId} 确认卡死，最后活动: {LastActivity}",
                    agentId, lastActivity);

                AgentStalled?.Invoke(this, new SubAgentStalledEventArgs
                {
                    AgentId = agentId,
                    Kind = StallKind.Single,
                    LastActivityAt = lastActivity,
                    Result = result,
                });
            }
        }

        // 链路卡死检测
        if (confirmedIds.Count >= _options.ChainStallThreshold)
        {
            var chainResults = _chainDetector.CheckAllChains(confirmedIds, parentMap);
            foreach (var chainResult in chainResults.Where(r => r.IsChainStalled))
            {
                _logger?.LogWarning("[SubAgentLivenessScanner] 链路卡死: {Chain}（{Confirmed}/{Total} 节点确认）",
                    string.Join("→", chainResult.Chain), chainResult.ConfirmedNodes, chainResult.TotalNodes);
                ChainStalled?.Invoke(this, chainResult);
            }
        }
    }

    /// <summary>
    /// 标记子代理已恢复 — 干预后调用
    /// </summary>
    public void MarkRecovered(string agentId)
    {
        if (_detectors.TryGetValue(agentId, out var detector))
            detector.MarkRecovered();
    }

    private static DateTimeOffset GetLastActivityAt(IAgent agent)
    {
        return agent is Entity entity ? entity.LastActivityAt : DateTimeOffset.UtcNow;
    }

    private static string GetSessionId(IAgent agent)
    {
        return agent is AgentBase ab ? ab.Context?.SessionId ?? agent.ObjectId.UniqueId : agent.ObjectId.UniqueId;
    }

    private static string? GetParentAgentId(IAgent agent)
    {
        return agent is AgentBase ab ? ab.Context?.ParentAgentId : null;
    }

    /// <summary>
    /// 释放扫描器资源
    /// </summary>
    public ValueTask DisposeAsync()
    {
        _stateMachine.StateChanged -= OnStateChanged;
        _cts.Cancel();
        _scanTimer?.Dispose();
        _cts.Dispose();
        _detectors.Clear();
        return ValueTask.CompletedTask;
    }
}
