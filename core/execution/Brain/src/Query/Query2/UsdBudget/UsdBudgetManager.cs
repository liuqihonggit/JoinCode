namespace Core.Query.UsdBudget;

public interface IUsdBudgetManager
{
    Task<bool> IsBudgetExceededAsync(CancellationToken ct = default);
    Task<UsdBudgetStatus> GetBudgetStatusAsync(CancellationToken ct = default);
    Task RecordCostAsync(decimal costUsd, string reason, CancellationToken ct = default);
    event EventHandler<UsdBudgetAlertEventArgs>? BudgetAlert;
}

public sealed partial class UsdBudgetStatus
{
    public required decimal MaxBudget { get; init; }
    public required decimal TotalUsed { get; init; }
    public required decimal Remaining { get; init; }
    public required double UsagePercentage { get; init; }
    public required bool IsExceeded { get; init; }
}

public sealed partial class UsdBudgetAlertEventArgs : EventArgs
{
    public required double UsagePercentage { get; init; }
    public required decimal TotalUsed { get; init; }
    public required decimal MaxBudget { get; init; }
    public required string Message { get; init; }
}

/// <summary>
/// USD 预算管理命令 — Actor 消息类型
/// </summary>
public interface IUsdBudgetCommand;

public sealed record IsBudgetExceededCmd(TaskCompletionSource<bool> Tcs) : IUsdBudgetCommand;
public sealed record GetBudgetStatusCmd(TaskCompletionSource<UsdBudgetStatus> Tcs) : IUsdBudgetCommand;
public sealed record RecordCostCmd(decimal CostUsd, string Reason, TaskCompletionSource Tcs) : IUsdBudgetCommand;

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

    public event EventHandler<UsdBudgetAlertEventArgs>? BudgetAlert;

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

    private static TaskCompletionSource<T> CreateTcs<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource CreateTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task<bool> IsBudgetExceededAsync(CancellationToken ct = default)
    {
        if (_config.MaxUsdBudget is not { } maxBudget || maxBudget <= 0)
        {
            return false;
        }
        var tcs = CreateTcs<bool>();
        await SendAsync(new IsBudgetExceededCmd(tcs), ct).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }

    public async Task<UsdBudgetStatus> GetBudgetStatusAsync(CancellationToken ct = default)
    {
        var tcs = CreateTcs<UsdBudgetStatus>();
        await SendAsync(new GetBudgetStatusCmd(tcs), ct).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }

    public async Task RecordCostAsync(decimal costUsd, string reason, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reason);
        if (_config.MaxUsdBudget is not { } maxBudget || maxBudget <= 0)
        {
            return;
        }
        var tcs = CreateTcs();
        await SendAsync(new RecordCostCmd(costUsd, reason, tcs), ct).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

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

    protected override void OnConsumerError(Exception ex)
    {
    }

    public override async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await base.DisposeAsync().ConfigureAwait(false);
    }
}
