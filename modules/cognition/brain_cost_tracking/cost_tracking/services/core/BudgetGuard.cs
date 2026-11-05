namespace Core.CostTracking;

/// <summary>成本快照 — 日/月/总成本,供 BudgetGuard 检查</summary>
internal readonly record struct CostSnapshot(decimal Daily, decimal Monthly, decimal Total);

/// <summary>
/// 预算守卫 — 封装预算配置、阈值告警、预算锁
/// 从 CostTracker 提取,通过 getCosts 回调获取成本数据,不直接依赖用量存储
/// </summary>
internal sealed class BudgetGuard {
    private BudgetState _state = new(null, ImmutableHashSet<double>.Empty);
    private readonly ILogger? _logger;

    private sealed record BudgetState(BudgetConfig? Config, ImmutableHashSet<double> TriggeredThresholds);

    /// <summary>预算告警触发事件</summary>
    public event EventHandler<CostAlertEventArgs>? CostAlertTriggered;

    /// <summary>构造 BudgetGuard</summary>
    public BudgetGuard(ILogger? logger = null, BudgetConfig? budgetConfig = null) {
        _logger = logger;
        _state = new BudgetState(budgetConfig, ImmutableHashSet<double>.Empty);
    }

    /// <summary>预算是否启用</summary>
    public bool IsEnabled => Volatile.Read(ref _state).Config?.Enabled == true;

    /// <summary>预算配置</summary>
    public BudgetConfig? Config => Volatile.Read(ref _state).Config;

    /// <summary>检查预算是否超限</summary>
    public bool IsExceeded(Func<CostSnapshot> getCosts) {
        if (Volatile.Read(ref _state).Config?.Enabled != true) return false;
        return GetStatus(getCosts).IsAnyBudgetExceeded();
    }

    /// <summary>获取预算状态</summary>
    public BudgetStatus GetStatus(Func<CostSnapshot> getCosts) {
        var config = Volatile.Read(ref _state).Config;
        if (config == null) {
            return new BudgetStatus {
                DailyUsed = 0,
                DailyLimit = 0,
                MonthlyUsed = 0,
                MonthlyLimit = 0
            };
        }

        var costs = getCosts();
        return new BudgetStatus {
            DailyUsed = costs.Daily,
            DailyLimit = config.DailyLimit,
            MonthlyUsed = costs.Monthly,
            MonthlyLimit = config.MonthlyLimit
        };
    }

    /// <summary>更新预算配置 — 重置已触发阈值集合</summary>
    public Task SetAsync(BudgetConfig config, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(config);
        config.ValidateOrThrow();

        Interlocked.Exchange(ref _state, new BudgetState(config, ImmutableHashSet<double>.Empty));

        _logger?.LogInformation("[CostTracker] 预算配置已更新 - 日限额: ${Daily}, 月限额: ${Monthly}, 总限额: ${Total}",
            config.DailyLimit, config.MonthlyLimit, config.TotalLimit);
        return Task.CompletedTask;
    }

    /// <summary>检查预算告警 — 通过回调获取成本快照</summary>
    public Task CheckAlertsAsync(Func<CostSnapshot> getCosts, CancellationToken ct = default) {
        var state = Volatile.Read(ref _state);
        if (state.Config?.Enabled != true || state.Config.AlertThresholds.Count == 0) {
            return Task.CompletedTask;
        }

        var costs = getCosts();
        CheckThresholdAlert(costs.Daily, state.Config.DailyLimit, BudgetType.Daily, state);
        CheckThresholdAlert(costs.Monthly, state.Config.MonthlyLimit, BudgetType.Monthly, state);
        CheckThresholdAlert(costs.Total, state.Config.TotalLimit, BudgetType.Total, state);
        return Task.CompletedTask;
    }

    /// <summary>重置已触发阈值</summary>
    public void Reset() {
        while (true) {
            var current = Volatile.Read(ref _state);
            var newState = current with { TriggeredThresholds = ImmutableHashSet<double>.Empty };
            if (Interlocked.CompareExchange(ref _state, newState, current) == current)
                break;
        }
    }

    /// <summary>释放资源</summary>
    public void Dispose() { }

    private void CheckThresholdAlert(decimal currentCost, decimal budgetLimit, BudgetType budgetType, BudgetState state) {
        if (budgetLimit <= 0) {
            return;
        }

        var budgetConfig = state.Config ?? throw new InvalidOperationException("BudgetConfig not available.");
        var percentageUsed = (double)(currentCost / budgetLimit);

        foreach (var threshold in budgetConfig.AlertThresholds) {
            if (percentageUsed < threshold) continue;
            while (true) {
                var current = Volatile.Read(ref _state);
                if (current.TriggeredThresholds.Contains(threshold)) break;
                var newState = current with { TriggeredThresholds = current.TriggeredThresholds.Add(threshold) };
                if (Interlocked.CompareExchange(ref _state, newState, current) == current) {
                    var level = threshold switch {
                        >= 1.0 => CostAlertLevel.Critical,
                        >= 0.8 => CostAlertLevel.Warning,
                        _ => CostAlertLevel.Info
                    };

                    var message = $"{budgetType.ToValue()}预算告警: 已使用 {percentageUsed:P1} (限额: ${budgetLimit:F2})";

                    var alert = CostAlert.Create(level, message, currentCost, budgetLimit);
                    var args = CostAlertEventArgs.Create(alert);

                    _logger?.LogWarning("[CostTracker] {Message}", message);
                    CostAlertTriggered?.Invoke(this, args);
                    return;
                }
            }
        }
    }
}
