namespace Core.Agents.Coordinator.Liveness;

/// <summary>
/// 子代理卡死防护协调器 — 纵深防御集成层（ADR 0106）
/// <para>
/// 组装 L2 检测 + L3 干预 + L4 恢复，提供统一启动/停止接口：
/// 1. 启动 SubAgentLivenessScanner 后台扫描
/// 2. 订阅 AgentStalled 事件 → SubAgentActivator 激活 → 等待恢复 → 仍卡死则 ProgressiveCompactor 压缩
/// 3. 订阅 ChainStalled 事件 → ProgressiveCompactor 压缩链上所有节点
/// 4. 暴露 TryPreemptAsync 供上层抢塞新任务
/// </para>
/// </summary>
public sealed partial class SubAgentStallDefenseCoordinator : IAsyncDisposable
{
    private readonly SubAgentLivenessScanner _scanner;
    private readonly SubAgentActivator _activator;
    private readonly ProgressiveCompactor _compactor;
    private readonly PreemptiveScheduler _preemptiveScheduler;
    private readonly SubAgentLivenessOptions _options;
    private readonly ILogger? _logger;
    private readonly Func<DateTimeOffset> _clock;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _activationTimes = new();

    /// <summary>
    /// 构造子代理卡死防护协调器
    /// </summary>
    public SubAgentStallDefenseCoordinator(
        SubAgentLivenessScanner scanner,
        SubAgentActivator activator,
        ProgressiveCompactor compactor,
        PreemptiveScheduler preemptiveScheduler,
        SubAgentLivenessOptions options,
        ILogger? logger = null,
        Func<DateTimeOffset>? clock = null)
    {
        _scanner = scanner ?? throw new ArgumentNullException(nameof(scanner));
        _activator = activator ?? throw new ArgumentNullException(nameof(activator));
        _compactor = compactor ?? throw new ArgumentNullException(nameof(compactor));
        _preemptiveScheduler = preemptiveScheduler ?? throw new ArgumentNullException(nameof(preemptiveScheduler));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger;
        _clock = clock ?? (() => DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// 启动卡死防护 — 订阅事件 + 启动扫描器
    /// </summary>
    public void Start()
    {
        _scanner.AgentStalled += OnAgentStalled;
        _scanner.ChainStalled += OnChainStalled;
        _scanner.Start();
        _logger?.LogInformation("[SubAgentStallDefense] 纵深防御体系已启动");
    }

    /// <summary>
    /// 尝试抢塞新任务到已完成的子代理
    /// </summary>
    public Task<PreemptResult> TryPreemptAsync(string taskDescription, CancellationToken ct = default)
    {
        return _preemptiveScheduler.TryPreemptAsync(taskDescription, ct);
    }

    /// <summary>
    /// 标记子代理已恢复 — 干预后调用
    /// </summary>
    public void MarkRecovered(string agentId)
    {
        _scanner.MarkRecovered(agentId);
        _activationTimes.TryRemove(agentId, out _);
    }

    /// <summary>
    /// 单点卡死事件处理 — 激活 → 等待恢复 → 仍卡死则压缩
    /// </summary>
    private void OnAgentStalled(object? sender, SubAgentStalledEventArgs e)
    {
        _ = Task.Run(() => HandleAgentStalledAsync(e));
    }

    /// <summary>
    /// 单点卡死处理逻辑
    /// </summary>
    private async Task HandleAgentStalledAsync(SubAgentStalledEventArgs e)
    {
        try
        {
            var agentId = e.AgentId;

            // 检查是否已在激活等待中（避免重复激活）
            if (_activationTimes.ContainsKey(agentId)) return;

            // L3 激活
            _activationTimes[agentId] = _clock();
            var activationResult = await _activator.ActivateAsync(agentId, _options.IdleThresholdSeconds).ConfigureAwait(false);

            if (!activationResult.Success)
            {
                _logger?.LogWarning("[SubAgentStallDefense] Agent {AgentId} 激活失败: {Reason}",
                    agentId, activationResult.Reason);
                // 激活失败，直接走 L4 压缩
                await _compactor.CompactProgressiveAsync(agentId).ConfigureAwait(false);
                return;
            }

            // 等待恢复（由 Scanner 在下次扫描时检测到活动后调用 MarkRecovered）
            _ = Task.Run(() => WaitForRecoveryAsync(agentId));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "[SubAgentStallDefense] HandleAgentStalledAsync 异常: {AgentId}", e.AgentId);
        }
    }

    /// <summary>
    /// 等待子代理恢复 — 超时后触发 L4 压缩
    /// </summary>
    private async Task WaitForRecoveryAsync(string agentId)
    {
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(_options.ActivationRecoverySeconds)).ConfigureAwait(false);

            // 检查是否已恢复
            if (!_activationTimes.ContainsKey(agentId)) return;

            // 仍未恢复，触发 L4 渐进式压缩
            _logger?.LogWarning("[SubAgentStallDefense] Agent {AgentId} 激活后 {Seconds}s 仍未恢复，触发 L4 压缩",
                agentId, _options.ActivationRecoverySeconds);

            var result = await _compactor.CompactProgressiveAsync(agentId).ConfigureAwait(false);
            _activationTimes.TryRemove(agentId, out _);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[SubAgentStallDefense] WaitForRecoveryAsync 异常: {AgentId}", agentId);
        }
    }

    /// <summary>
    /// 链路卡死事件处理 — 压缩链上所有 Confirmed 节点
    /// </summary>
    private void OnChainStalled(object? sender, ChainStallResult e)
    {
        _ = Task.Run(() => HandleChainStalledAsync(e));
    }

    /// <summary>
    /// 链路卡死处理逻辑
    /// </summary>
    private async Task HandleChainStalledAsync(ChainStallResult e)
    {
        try
        {
            _logger?.LogWarning("[SubAgentStallDefense] 链路卡死: {Chain}，开始压缩所有节点",
                string.Join("→", e.Chain));

            // 从叶子到根依次压缩
            foreach (var agentId in e.Chain)
            {
                await _compactor.CompactProgressiveAsync(agentId).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "[SubAgentStallDefense] HandleChainStalledAsync 异常");
        }
    }

    /// <summary>
    /// 释放协调器资源
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        _scanner.AgentStalled -= OnAgentStalled;
        _scanner.ChainStalled -= OnChainStalled;
        await _scanner.DisposeAsync().ConfigureAwait(false);
        _activationTimes.Clear();
    }
}
