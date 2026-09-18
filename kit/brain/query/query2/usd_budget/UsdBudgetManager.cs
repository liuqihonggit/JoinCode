namespace Core.Query.UsdBudget;

/// <summary>
/// USD 预算管理器接口 — 通过 Actor 模型实现线程安全的预算查询与记录
/// </summary>
public interface IUsdBudgetManager
{
    /// <summary>
    /// 检查 USD 预算是否已超限
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>已超限返回 true，否则返回 false</returns>
    Task<bool> IsBudgetExceededAsync(CancellationToken ct = default);

    /// <summary>
    /// 获取当前 USD 预算状态快照
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>预算状态</returns>
    Task<UsdBudgetStatus> GetBudgetStatusAsync(CancellationToken ct = default);

    /// <summary>
    /// 记录一笔 USD 成本消耗
    /// </summary>
    /// <param name="costUsd">成本金额（美元）</param>
    /// <param name="reason">消耗原因</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    Task RecordCostAsync(decimal costUsd, string reason, CancellationToken ct = default);

    /// <summary>
    /// 预算告警事件 — 使用率达到阈值时触发
    /// </summary>
    event EventHandler<UsdBudgetAlertEventArgs>? BudgetAlert;
}

/// <summary>
/// USD 预算状态快照
/// </summary>
public sealed partial class UsdBudgetStatus
{
    /// <summary>
    /// 预算上限
    /// </summary>
    public required decimal MaxBudget { get; init; }

    /// <summary>
    /// 已使用金额
    /// </summary>
    public required decimal TotalUsed { get; init; }

    /// <summary>
    /// 剩余金额
    /// </summary>
    public required decimal Remaining { get; init; }

    /// <summary>
    /// 使用率（0.0-1.0）
    /// </summary>
    public required double UsagePercentage { get; init; }

    /// <summary>
    /// 是否已超限
    /// </summary>
    public required bool IsExceeded { get; init; }
}

/// <summary>
/// USD 预算告警事件参数
/// </summary>
public sealed partial class UsdBudgetAlertEventArgs : EventArgs
{
    /// <summary>
    /// 使用率（0.0-1.0）
    /// </summary>
    public required double UsagePercentage { get; init; }

    /// <summary>
    /// 已使用金额
    /// </summary>
    public required decimal TotalUsed { get; init; }

    /// <summary>
    /// 预算上限
    /// </summary>
    public required decimal MaxBudget { get; init; }

    /// <summary>
    /// 告警消息
    /// </summary>
    public required string Message { get; init; }
}

/// <summary>
/// USD 预算管理命令 — Actor 消息类型
/// </summary>
public interface IUsdBudgetCommand;

/// <summary>
/// 检查预算是否超限命令
/// </summary>
/// <param name="Tcs">结果回源</param>
public sealed record IsBudgetExceededCmd(TaskCompletionSource<bool> Tcs) : IUsdBudgetCommand;

/// <summary>
/// 获取预算状态命令
/// </summary>
/// <param name="Tcs">结果回源</param>
public sealed record GetBudgetStatusCmd(TaskCompletionSource<UsdBudgetStatus> Tcs) : IUsdBudgetCommand;

/// <summary>
/// 记录成本命令
/// </summary>
/// <param name="CostUsd">成本金额（美元）</param>
/// <param name="Reason">消耗原因</param>
/// <param name="Tcs">结果回源</param>
public sealed record RecordCostCmd(decimal CostUsd, string Reason, TaskCompletionSource Tcs) : IUsdBudgetCommand;

/// <summary>
/// USD 预算管理器实现 — Actor 化：Consumer 线程独占 _totalUsed/_alertTriggered，消除 AsyncLock
/// </summary>
[Register(typeof(IUsdBudgetManager), ServiceLifetime.Singleton)]
public sealed partial class UsdBudgetManager : ActorBase<IUsdBudgetCommand, Unit>, IUsdBudgetManager, IAsyncDisposable
{
    private readonly ICostTracker _costTracker;
    private readonly QueryEngineConfig _config;
    private readonly ILogger<UsdBudgetManager>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private decimal _totalUsed;
    private bool _alertTriggered;
    private int _disposed;

    /// <summary>
    /// 预算告警事件 — 使用率达到阈值时触发
    /// </summary>
    public event EventHandler<UsdBudgetAlertEventArgs>? BudgetAlert;

    /// <summary>
    /// 构造函数 — 注入成本追踪器、配置、日志和遥测服务
    /// </summary>
    /// <param name="costTracker">成本追踪器</param>
    /// <param name="configOptions">查询引擎配置选项</param>
    /// <param name="logger">日志记录器</param>
    /// <param name="telemetryService">遥测服务</param>
    public UsdBudgetManager(
        ICostTracker costTracker,
        IOptions<QueryEngineConfig> configOptions,
        ILogger<UsdBudgetManager>? logger = null,
        ITelemetryService? telemetryService = null)
        : base()
    {
        _costTracker = costTracker ?? throw new ArgumentNullException(nameof(costTracker));
        _config = configOptions?.Value ?? new QueryEngineConfig();
        _logger = logger;
        _telemetryService = telemetryService;
        _totalUsed = 0m;
        _alertTriggered = false;
    }


    /// <summary>
    /// 检查 USD 预算是否已超限
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>已超限返回 true，否则返回 false</returns>
    public async Task<bool> IsBudgetExceededAsync(CancellationToken ct = default)
    {
        if (_config.MaxUsdBudget is not { } maxBudget || maxBudget <= 0)
        {
            return false;
        }
        var tcs = TcsFactory.Create<bool>();
        await SendAsync(new IsBudgetExceededCmd(tcs), ct).ConfigureAwait(false);
        return await AskAwait(tcs, ct);
    }

    /// <summary>
    /// 获取当前 USD 预算状态快照
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>预算状态</returns>
    public async Task<UsdBudgetStatus> GetBudgetStatusAsync(CancellationToken ct = default)
    {
        var tcs = TcsFactory.Create<UsdBudgetStatus>();
        await SendAsync(new GetBudgetStatusCmd(tcs), ct).ConfigureAwait(false);
        return await AskAwait(tcs, ct);
    }

    /// <summary>
    /// 记录一笔 USD 成本消耗
    /// </summary>
    /// <param name="costUsd">成本金额（美元）</param>
    /// <param name="reason">消耗原因</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task RecordCostAsync(decimal costUsd, string reason, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reason);
        if (_config.MaxUsdBudget is not { } maxBudget || maxBudget <= 0)
        {
            return;
        }
        var tcs = TcsFactory.Create();
        await SendAsync(new RecordCostCmd(costUsd, reason, tcs), ct).ConfigureAwait(false);
        await AskAwait(tcs, ct);
    }

    /// <summary>
    /// 处理 Actor 消息 — 在 Consumer 线程独占执行
    /// </summary>
    /// <param name="command">命令消息</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的值任务</returns>
    protected override ValueTask HandleAsync(IUsdBudgetCommand command, CancellationToken ct)
    {
        switch (command)
        {
            case IsBudgetExceededCmd isExceeded:
                if (_config.MaxUsdBudget is { } maxBudget1 && maxBudget1 > 0)
                    isExceeded.Tcs.TrySetResult(_totalUsed >= maxBudget1);
                else
                    isExceeded.Tcs.TrySetResult(false);
                break;

            case GetBudgetStatusCmd getStatus:
                {
                    var maxBudget = _config.MaxUsdBudget ?? 0m;
                    var remaining = maxBudget > 0 ? Math.Max(0m, maxBudget - _totalUsed) : decimal.MaxValue;
                    var usagePercentage = maxBudget > 0 ? Math.Min(1.0, (double)(_totalUsed / maxBudget)) : 0.0;

                    getStatus.Tcs.TrySetResult(new UsdBudgetStatus
                    {
                        MaxBudget = maxBudget,
                        TotalUsed = _totalUsed,
                        Remaining = remaining,
                        UsagePercentage = usagePercentage,
                        IsExceeded = maxBudget > 0 && _totalUsed >= maxBudget
                    });
                }
                break;

            case RecordCostCmd recordCost:
                {
                    if (_config.MaxUsdBudget is not { } maxBudget2 || maxBudget2 <= 0)
                    {
                        recordCost.Tcs.TrySetResult();
                        return ValueTask.CompletedTask;
                    }

                    _totalUsed += recordCost.CostUsd;
                    _logger?.LogInformation("[UsdBudgetManager] Recorded cost: ${Cost:F6} for '{Reason}', total: ${Total:F6} / ${Max:F2}", recordCost.CostUsd, recordCost.Reason, _totalUsed, maxBudget2);
                    _telemetryService?.RecordCount("budget.cost.recorded", description: "Budget cost recorded count");
                    _telemetryService?.RecordHistogram("budget.cost.usd", (double)recordCost.CostUsd, unit: "usd", description: "Budget cost in USD");

                    var usagePercentage = (double)(_totalUsed / maxBudget2);
                    if (usagePercentage >= _config.UsdAlertThreshold && !_alertTriggered)
                    {
                        _alertTriggered = true;
                        var message = $"USD budget alert: used ${_totalUsed:F2} of ${maxBudget2:F2} ({usagePercentage:P1})";

                        _logger?.LogWarning("[UsdBudgetManager] {Message}", message);

                        BudgetAlert?.Invoke(this, new UsdBudgetAlertEventArgs
                        {
                            UsagePercentage = usagePercentage,
                            TotalUsed = _totalUsed,
                            MaxBudget = maxBudget2,
                            Message = message
                        });
                    }
                    recordCost.Tcs.TrySetResult();
                }
                break;
        }
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Consumer 线程异常回调 — 空实现，异常由 Actor 基类统一处理
    /// </summary>
    /// <param name="ex">异常对象</param>
    protected override void OnConsumerError(Exception ex)
    {
    }

    /// <summary>
    /// 异步释放资源
    /// </summary>
    /// <returns>表示异步操作的值任务</returns>
    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
