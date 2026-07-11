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

[Register(typeof(IUsdBudgetManager))]
public sealed partial class UsdBudgetManager : IUsdBudgetManager, IAsyncDisposable
{
    private readonly SemaphoreSlim _lock;
    private readonly ICostTracker _costTracker;
    private readonly QueryEngineConfig _config;
    [Inject] private readonly ILogger<UsdBudgetManager>? _logger;
    private readonly ITelemetryService? _telemetryService;
    private decimal _totalUsed;
    private bool _alertTriggered;

    public event EventHandler<UsdBudgetAlertEventArgs>? BudgetAlert;

    public UsdBudgetManager(
        ICostTracker costTracker,
        IOptions<QueryEngineConfig> configOptions,
        ILogger<UsdBudgetManager>? logger = null,
        ITelemetryService? telemetryService = null)
    {
        _costTracker = costTracker ?? throw new ArgumentNullException(nameof(costTracker));
        _config = configOptions?.Value ?? new QueryEngineConfig();
        _logger = logger;
        _telemetryService = telemetryService;
        _lock = new SemaphoreSlim(1, 1);
        _totalUsed = 0m;
        _alertTriggered = false;
    }

    public async Task<bool> IsBudgetExceededAsync(CancellationToken ct = default)
    {
        if (_config.MaxUsdBudget is not { } maxBudget || maxBudget <= 0)
        {
            return false;
        }

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _totalUsed >= maxBudget;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<UsdBudgetStatus> GetBudgetStatusAsync(CancellationToken ct = default)
    {
        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            var maxBudget = _config.MaxUsdBudget ?? 0m;
            var remaining = maxBudget > 0 ? Math.Max(0m, maxBudget - _totalUsed) : decimal.MaxValue;
            var usagePercentage = maxBudget > 0 ? Math.Min(1.0, (double)(_totalUsed / maxBudget)) : 0.0;

            return new UsdBudgetStatus
            {
                MaxBudget = maxBudget,
                TotalUsed = _totalUsed,
                Remaining = remaining,
                UsagePercentage = usagePercentage,
                IsExceeded = maxBudget > 0 && _totalUsed >= maxBudget
            };
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task RecordCostAsync(decimal costUsd, string reason, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reason);

        if (_config.MaxUsdBudget is not { } maxBudget || maxBudget <= 0)
        {
            return;
        }

        await _lock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _totalUsed += costUsd;
            _logger?.LogInformation("[UsdBudgetManager] Recorded cost: ${Cost:F6} for '{Reason}', total: ${Total:F6} / ${Max:F2}", costUsd, reason, _totalUsed, maxBudget);
            _telemetryService?.RecordCount("budget.cost.recorded", description: "Budget cost recorded count");
            _telemetryService?.RecordHistogram("budget.cost.usd", (double)costUsd, unit: "usd", description: "Budget cost in USD");

            var usagePercentage = (double)(_totalUsed / maxBudget);
            if (usagePercentage >= _config.UsdAlertThreshold && !_alertTriggered)
            {
                _alertTriggered = true;
                var message = $"USD budget alert: used ${_totalUsed:F2} of ${maxBudget:F2} ({usagePercentage:P1})";

                _logger?.LogWarning("[UsdBudgetManager] {Message}", message);

                BudgetAlert?.Invoke(this, new UsdBudgetAlertEventArgs
                {
                    UsagePercentage = usagePercentage,
                    TotalUsed = _totalUsed,
                    MaxBudget = maxBudget,
                    Message = message
                });
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lock.Dispose();
    }
}
