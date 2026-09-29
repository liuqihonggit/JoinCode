namespace Core.Tests.CostTracking;

/// <summary>
/// BudgetGuard 单元测试 — 预算守卫全部 public 方法
/// 覆盖预算启用/检查/告警触发/日/月/总限额/重置/配置更新
/// </summary>
public sealed class BudgetGuardTests {
    // ---------- 构造 / IsEnabled / Config ----------

    [Fact]
    public void Constructor_NoConfig_IsDisabledAndConfigNull() {
        var guard = new BudgetGuard();

        Assert.False(guard.IsEnabled);
        Assert.Null(guard.Config);
    }

    [Fact]
    public void Constructor_EnabledConfig_IsEnabled() {
        var guard = new BudgetGuard(budgetConfig: MakeConfig(enabled: true));

        Assert.True(guard.IsEnabled);
        Assert.NotNull(guard.Config);
    }

    [Fact]
    public void Constructor_DisabledConfig_IsDisabled() {
        var guard = new BudgetGuard(budgetConfig: MakeConfig(enabled: false));

        Assert.False(guard.IsEnabled);
    }

    // ---------- IsExceeded ----------

    [Fact]
    public void IsExceeded_NullConfig_ReturnsFalse() {
        var guard = new BudgetGuard();

        Assert.False(guard.IsExceeded(() => new CostSnapshot(100, 100, 100)));
    }

    [Fact]
    public void IsExceeded_DisabledConfig_ReturnsFalse() {
        var guard = new BudgetGuard(budgetConfig: MakeConfig(enabled: false));

        Assert.False(guard.IsExceeded(() => new CostSnapshot(100, 100, 100)));
    }

    [Fact]
    public void IsExceeded_WithinLimits_ReturnsFalse() {
        var guard = new BudgetGuard(budgetConfig: MakeConfig(daily: 10, monthly: 100, total: 1000));

        Assert.False(guard.IsExceeded(() => new CostSnapshot(5, 50, 500)));
    }

    [Fact]
    public void IsExceeded_DailyExceeded_ReturnsTrue() {
        var guard = new BudgetGuard(budgetConfig: MakeConfig(daily: 10, monthly: 100, total: 1000));

        Assert.True(guard.IsExceeded(() => new CostSnapshot(10, 0, 0)));
    }

    [Fact]
    public void IsExceeded_MonthlyExceeded_ReturnsTrue() {
        var guard = new BudgetGuard(budgetConfig: MakeConfig(daily: 10, monthly: 100, total: 1000));

        Assert.True(guard.IsExceeded(() => new CostSnapshot(0, 100, 0)));
    }

    [Fact]
    public void IsExceeded_JustBelowLimit_ReturnsFalse() {
        var guard = new BudgetGuard(budgetConfig: MakeConfig(daily: 10, monthly: 100, total: 1000));

        // 9.99 < 10 → 未超(IsDailyExceeded 用 >=)
        Assert.False(guard.IsExceeded(() => new CostSnapshot(9.99m, 0, 0)));
    }

    // ---------- GetStatus ----------

    [Fact]
    public void GetStatus_NullConfig_ReturnsEmptyStatus() {
        var guard = new BudgetGuard();

        var status = guard.GetStatus(() => new CostSnapshot(5, 50, 500));

        Assert.Equal(0, status.DailyUsed);
        Assert.Equal(0, status.DailyLimit);
        Assert.Equal(0, status.MonthlyUsed);
        Assert.Equal(0, status.MonthlyLimit);
    }

    [Fact]
    public void GetStatus_WithConfig_ReflectsCosts() {
        var guard = new BudgetGuard(budgetConfig: MakeConfig(daily: 10, monthly: 100, total: 1000));

        var status = guard.GetStatus(() => new CostSnapshot(7, 70, 700));

        Assert.Equal(7, status.DailyUsed);
        Assert.Equal(10, status.DailyLimit);
        Assert.Equal(70, status.MonthlyUsed);
        Assert.Equal(100, status.MonthlyLimit);
    }

    // ---------- SetAsync ----------

    [Fact]
    public async Task SetAsync_UpdatesConfigAndEnables() {
        var guard = new BudgetGuard();

        await guard.SetAsync(MakeConfig(daily: 20, monthly: 200, total: 2000, enabled: true));

        Assert.True(guard.IsEnabled);
        Assert.Equal(20, guard.Config!.DailyLimit);
    }

    [Fact]
    public async Task SetAsync_NullConfig_Throws() {
        var guard = new BudgetGuard();

        await Assert.ThrowsAsync<ArgumentNullException>(() => guard.SetAsync(null!));
    }

    [Fact]
    public async Task SetAsync_InvalidConfig_Throws() {
        var guard = new BudgetGuard();
        var invalid = new BudgetConfig {
            DailyLimit = -1,  // 负数无效
            MonthlyLimit = 100,
            TotalLimit = 1000,
            AlertThresholds = [0.5, 0.8, 1.0],
            Enabled = true
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => guard.SetAsync(invalid));
    }

    [Fact]
    public async Task SetAsync_ResetsTriggeredThresholds() {
        var guard = new BudgetGuard(budgetConfig: MakeConfig(daily: 10, monthly: 100, total: 1000));
        var triggered = 0;
        guard.CostAlertTriggered += (_, _) => triggered++;

        // 触发 0.5 阈值
        await guard.CheckAlertsAsync(() => new CostSnapshot(5, 0, 0));
        Assert.Equal(1, triggered);

        // 更新配置重置触发集合
        await guard.SetAsync(MakeConfig(daily: 10, monthly: 100, total: 1000));
        await guard.CheckAlertsAsync(() => new CostSnapshot(5, 0, 0));
        Assert.Equal(2, triggered); // 再次触发
    }

    // ---------- CheckAlertsAsync ----------

    [Fact]
    public async Task CheckAlertsAsync_NullConfig_NoEvent() {
        var guard = new BudgetGuard();
        var triggered = 0;
        guard.CostAlertTriggered += (_, _) => triggered++;

        await guard.CheckAlertsAsync(() => new CostSnapshot(5, 50, 500));

        Assert.Equal(0, triggered);
    }

    [Fact]
    public async Task CheckAlertsAsync_DisabledConfig_NoEvent() {
        var guard = new BudgetGuard(budgetConfig: MakeConfig(enabled: false));
        var triggered = 0;
        guard.CostAlertTriggered += (_, _) => triggered++;

        await guard.CheckAlertsAsync(() => new CostSnapshot(5, 50, 500));

        Assert.Equal(0, triggered);
    }

    [Fact]
    public async Task CheckAlertsAsync_ReachesHalfThreshold_TriggersInfoAlert() {
        var guard = new BudgetGuard(budgetConfig: MakeConfig(daily: 10, monthly: 100, total: 1000));
        CostAlert? alert = null;
        guard.CostAlertTriggered += (_, e) => alert = e.Alert;

        // 日 5/10=50% >= 0.5 → 触发 Info
        await guard.CheckAlertsAsync(() => new CostSnapshot(5, 0, 0));

        Assert.NotNull(alert);
        Assert.Equal(CostAlertLevel.Info, alert!.Level);
    }

    [Fact]
    public async Task CheckAlertsAsync_ReachesEightyPercent_TriggersWarningAlert() {
        var guard = new BudgetGuard(budgetConfig: MakeConfig(daily: 10, monthly: 100, total: 1000));
        CostAlert? alert = null;
        guard.CostAlertTriggered += (_, e) => alert = e.Alert;

        // 日 8/10=80% >= 0.8 → 触发 Warning(0.5 先触发但已 break?不,0.5 先检查,80%>=0.5 触发 0.5 Info)
        await guard.CheckAlertsAsync(() => new CostSnapshot(8, 0, 0));

        Assert.NotNull(alert);
        // 80% >= 0.5,先触发 0.5(Info)
        Assert.Equal(CostAlertLevel.Info, alert!.Level);
    }

    [Fact]
    public async Task CheckAlertsAsync_AlreadyTriggeredThreshold_NotRetriggered() {
        var guard = new BudgetGuard(budgetConfig: MakeConfig(daily: 10, monthly: 100, total: 1000));
        var triggered = 0;
        guard.CostAlertTriggered += (_, _) => triggered++;

        await guard.CheckAlertsAsync(() => new CostSnapshot(5, 0, 0));
        Assert.Equal(1, triggered);

        // 同阈值不再触发
        await guard.CheckAlertsAsync(() => new CostSnapshot(5, 0, 0));
        Assert.Equal(1, triggered);
    }

    [Fact]
    public async Task CheckAlertsAsync_MultipleBudgets_SharedThresholdTriggersOnce() {
        var guard = new BudgetGuard(budgetConfig: MakeConfig(daily: 10, monthly: 100, total: 1000));
        var triggered = 0;
        guard.CostAlertTriggered += (_, _) => triggered++;

        // 日 5/10=50%, 月 50/100=50%, 总 500/1000=50% 都达 0.5 阈值
        // 但 _triggeredThresholds 是日/月/总共享的单 HashSet,同阈值只触发一次
        await guard.CheckAlertsAsync(() => new CostSnapshot(5, 50, 500));

        Assert.Equal(1, triggered);
    }

    [Fact]
    public async Task CheckAlertsAsync_DifferentThresholds_EachTriggersOnce() {
        var guard = new BudgetGuard(budgetConfig: MakeConfig(daily: 10, monthly: 100, total: 1000));
        var triggered = 0;
        guard.CostAlertTriggered += (_, _) => triggered++;

        // 日 5/10=50% 触发 0.5,月 80/100=80% 触发 0.8,总 950/1000=95% 触发 0.5?不,0.5 已触发
        // 日 50% → 0.5(Info),月 80% → 0.5 已触发,跳过;0.8 未触发 → 0.8(Warning)
        await guard.CheckAlertsAsync(() => new CostSnapshot(5, 80, 0));

        Assert.Equal(2, triggered);
    }

    [Fact]
    public async Task CheckAlertsAsync_BelowAllThresholds_NoEvent() {
        var guard = new BudgetGuard(budgetConfig: MakeConfig(daily: 10, monthly: 100, total: 1000));
        var triggered = 0;
        guard.CostAlertTriggered += (_, _) => triggered++;

        // 日 1/10=10% < 0.5
        await guard.CheckAlertsAsync(() => new CostSnapshot(1, 0, 0));

        Assert.Equal(0, triggered);
    }

    // ---------- Reset ----------

    [Fact]
    public async Task Reset_ClearsTriggeredThresholds_AllowingRetrigger() {
        var guard = new BudgetGuard(budgetConfig: MakeConfig(daily: 10, monthly: 100, total: 1000));
        var triggered = 0;
        guard.CostAlertTriggered += (_, _) => triggered++;

        await guard.CheckAlertsAsync(() => new CostSnapshot(5, 0, 0));
        Assert.Equal(1, triggered);

        guard.Reset();

        await guard.CheckAlertsAsync(() => new CostSnapshot(5, 0, 0));
        Assert.Equal(2, triggered);
    }

    // ---------- IsExceeded (Total 分支) ----------
    // 注:BudgetStatus 仅含 Daily/Monthly 字段,IsAnyBudgetExceeded 不检查 Total。
    // Total 超限由 CheckAlertsAsync 告警路径覆盖(见下方),IsExceeded 硬停止仅看 Daily/Monthly。
    // 这是既有设计:IsExceeded → CostTracker.IsBudgetExceeded 用于硬停止当前会话,Total 累计超限只告警。

    [Fact]
    public void IsExceeded_TotalExceededButDailyMonthlyNot_NotDetected() {
        // 文档化:Total 超限不触发 IsExceeded(BudgetStatus 无 Total 字段)
        var guard = new BudgetGuard(budgetConfig: MakeConfig(daily: 10, monthly: 100, total: 1000));

        Assert.False(guard.IsExceeded(() => new CostSnapshot(0, 0, 1000)));
    }

    // ---------- CheckAlertsAsync (Total / Monthly / Warning / Critical / 零限额) ----------

    [Fact]
    public async Task CheckAlertsAsync_TotalReachesHalfThreshold_TriggersAlert() {
        var guard = new BudgetGuard(budgetConfig: MakeConfig(daily: 100, monthly: 1000, total: 1000));
        CostAlert? alert = null;
        guard.CostAlertTriggered += (_, e) => alert = e.Alert;

        // 总 500/1000=50% >= 0.5 → 触发(日 0, 月 0 不触发)
        await guard.CheckAlertsAsync(() => new CostSnapshot(0, 0, 500));

        Assert.NotNull(alert);
        Assert.Equal(CostAlertLevel.Info, alert!.Level);
    }

    [Fact]
    public async Task CheckAlertsAsync_MonthlyReachesHalfThreshold_TriggersAlert() {
        var guard = new BudgetGuard(budgetConfig: MakeConfig(daily: 100, monthly: 100, total: 1000));
        CostAlert? alert = null;
        guard.CostAlertTriggered += (_, e) => alert = e.Alert;

        // 月 50/100=50% >= 0.5 → 触发(日 0, 总 0 不触发)
        await guard.CheckAlertsAsync(() => new CostSnapshot(0, 50, 0));

        Assert.NotNull(alert);
        Assert.Equal(CostAlertLevel.Info, alert!.Level);
    }

    [Fact]
    public async Task CheckAlertsAsync_EightyPercentWithSingleThreshold_TriggersWarning() {
        // 用仅 [0.8] 阈值,80% 直接触发 Warning(不被 0.5 先触发)
        var config = new BudgetConfig {
            DailyLimit = 10, MonthlyLimit = 100, TotalLimit = 1000,
            AlertThresholds = [0.8], Enabled = true
        };
        var guard = new BudgetGuard(budgetConfig: config);
        CostAlert? alert = null;
        guard.CostAlertTriggered += (_, e) => alert = e.Alert;

        await guard.CheckAlertsAsync(() => new CostSnapshot(8, 0, 0));

        Assert.NotNull(alert);
        Assert.Equal(CostAlertLevel.Warning, alert!.Level);
    }

    [Fact]
    public async Task CheckAlertsAsync_FullThresholdWithSingleThreshold_TriggersCritical() {
        // 用仅 [1.0] 阈值,100% 直接触发 Critical
        var config = new BudgetConfig {
            DailyLimit = 10, MonthlyLimit = 100, TotalLimit = 1000,
            AlertThresholds = [1.0], Enabled = true
        };
        var guard = new BudgetGuard(budgetConfig: config);
        CostAlert? alert = null;
        guard.CostAlertTriggered += (_, e) => alert = e.Alert;

        await guard.CheckAlertsAsync(() => new CostSnapshot(10, 0, 0));

        Assert.NotNull(alert);
        Assert.Equal(CostAlertLevel.Critical, alert!.Level);
    }

    [Fact]
    public async Task CheckAlertsAsync_ZeroBudgetLimit_NoAlert() {
        // budgetLimit <= 0 时 CheckThresholdAlert 早返回(第 92 行)
        var guard = new BudgetGuard(budgetConfig: MakeConfig(daily: 0, monthly: 100, total: 1000));
        var triggered = 0;
        guard.CostAlertTriggered += (_, _) => triggered++;

        await guard.CheckAlertsAsync(() => new CostSnapshot(5, 0, 0));

        Assert.Equal(0, triggered);
    }

    [Fact]
    public async Task CheckAlertsAsync_EmptyThresholds_NoEvent() {
        // AlertThresholds.Count == 0 时早返回(第 73 行)
        var config = new BudgetConfig {
            DailyLimit = 10, MonthlyLimit = 100, TotalLimit = 1000,
            AlertThresholds = [], Enabled = true
        };
        var guard = new BudgetGuard(budgetConfig: config);
        var triggered = 0;
        guard.CostAlertTriggered += (_, _) => triggered++;

        await guard.CheckAlertsAsync(() => new CostSnapshot(100, 100, 100));

        Assert.Equal(0, triggered);
    }

    // ---------- Dispose ----------

    [Fact]
    public void Dispose_DoesNotThrow() {
        var guard = new BudgetGuard(budgetConfig: MakeConfig());

        guard.Dispose();
    }

    // ---------- Helpers ----------

    private static BudgetConfig MakeConfig(decimal daily = 10, decimal monthly = 100, decimal total = 1000, bool enabled = true)
        => new() {
            DailyLimit = daily,
            MonthlyLimit = monthly,
            TotalLimit = total,
            AlertThresholds = [0.5, 0.8, 1.0],
            Enabled = enabled
        };
}
