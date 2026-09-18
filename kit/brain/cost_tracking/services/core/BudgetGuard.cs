namespace Core.CostTracking;

/// <summary>成本快照 — 日/月/总成本,供 BudgetGuard 检查</summary>
internal readonly record struct CostSnapshot(decimal Daily, decimal Monthly, decimal Total);

/// <summary>
/// 预算守卫 — 封装预算配置、阈值告警、预算锁
/// 从 CostTracker 提取,通过 getCosts 回调获取成本数据,不直接依赖用量存储
/// </summary>
internal sealed class BudgetGuard
{
    private BudgetConfig? _budgetConfig;
    private readonly HashSet<double> _triggeredThresholds = [];
    private readonly AsyncLock _budgetLock = new();
    private readonly ILogger? _logger;

    /// <summary>预算告警触发事件</summary>
    public event EventHandler<CostAlertEventArgs>? CostAlertTriggered;

    /// <summary>构造 BudgetGuard</summary>
    public BudgetGuard(ILogger? logger = null, BudgetConfig? budgetConfig = null)
    {
        _logger = logger;
        _budgetConfig = budgetConfig;
    }

    /// <summary>预算是否启用</summary>
    public bool IsEnabled => _budgetConfig?.Enabled == true;

    /// <summary>预算配置</summary>
    public BudgetConfig? Config => _budgetConfig;

    /// <summary>检查预算是否超限</summary>
    public bool IsExceeded(Func<CostSnapshot> getCosts)
    {
        if (_budgetConfig?.Enabled != true) return false;
        return GetStatus(getCosts).IsAnyBudgetExceeded();
    }

    /// <summary>获取预算状态</summary>
    public BudgetStatus GetStatus(Func<CostSnapshot> getCosts)
    {
        if (_budgetConfig == null)
        {
            return new BudgetStatus
            {
                DailyUsed = 0,
                DailyLimit = 0,
                MonthlyUsed = 0,
                MonthlyLimit = 0
            };
        }

        var costs = getCosts();
        return new BudgetStatus
        {
            DailyUsed = costs.Daily,
            DailyLimit = _budgetConfig.DailyLimit,
            MonthlyUsed = costs.Monthly,
            MonthlyLimit = _budgetConfig.MonthlyLimit
        };
    }

    /// <summary>异步更新预算配置 — 加锁保护,重置已触发阈值集合</summary>
    public async Task SetAsync(BudgetConfig config, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(config);
        config.ValidateOrThrow();

        using var guard = await _budgetLock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_budgetLock.Name}' 等待超时");

        _budgetConfig = config;
        _triggeredThresholds.Clear();

        _logger?.LogInformation("[CostTracker] 预算配置已更新 - 日限额: ${Daily}, 月限额: ${Monthly}, 总限额: ${Total}",
            config.DailyLimit, config.MonthlyLimit, config.TotalLimit);
    }

    /// <summary>检查预算告警 — 通过回调获取成本快照</summary>
    public async Task CheckAlertsAsync(Func<CostSnapshot> getCosts, CancellationToken ct = default)
    {
        if (_budgetConfig?.Enabled != true || _budgetConfig.AlertThresholds.Count == 0)
        {
            return;
        }

        using var guard = await _budgetLock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_budgetLock.Name}' 等待超时");

        var costs = getCosts();
        CheckThresholdAlert(costs.Daily, _budgetConfig.DailyLimit, BudgetType.Daily);
        CheckThresholdAlert(costs.Monthly, _budgetConfig.MonthlyLimit, BudgetType.Monthly);
        CheckThresholdAlert(costs.Total, _budgetConfig.TotalLimit, BudgetType.Total);
    }

    /// <summary>重置已触发阈值</summary>
    public void Reset() => _triggeredThresholds.Clear();

    /// <summary>释放预算锁</summary>
    public void Dispose() => _budgetLock.Dispose();

    private void CheckThresholdAlert(decimal currentCost, decimal budgetLimit, BudgetType budgetType)
    {
        if (budgetLimit <= 0)
        {
            return;
        }

        var budgetConfig = _budgetConfig ?? throw new InvalidOperationException("BudgetConfig not available.");
        var percentageUsed = (double)(currentCost / budgetLimit);

        foreach (var threshold in budgetConfig.AlertThresholds)
        {
            if (percentageUsed >= threshold && !_triggeredThresholds.Contains(threshold))
            {
                _triggeredThresholds.Add(threshold);

                var level = threshold switch
                {
                    >= 1.0 => CostAlertLevel.Critical,
                    >= 0.8 => CostAlertLevel.Warning,
                    _ => CostAlertLevel.Info
                };

                var message = $"{budgetType}预算告警: 已使用 {percentageUsed:P1} (限额: ${budgetLimit:F2})";

                var alert = CostAlert.Create(level, message, currentCost, budgetLimit);
                var args = CostAlertEventArgs.Create(alert);

                _logger?.LogWarning("[CostTracker] {Message}", message);
                CostAlertTriggered?.Invoke(this, args);

                break;
            }
        }
    }
}
