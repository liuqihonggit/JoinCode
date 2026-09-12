
namespace Core.Query;

/// <summary>
/// Token预算管理命令 — Actor 消息类型
/// </summary>
public interface ITokenBudgetCommand;

public sealed record AllocateBudgetCmd(long Amount, TaskCompletionSource Tcs) : ITokenBudgetCommand;
public sealed record ConsumeTokensCmd(long Amount, string Reason, string? ToolName, TaskCompletionSource Tcs) : ITokenBudgetCommand;
public sealed record GetRemainingBudgetCmd(TaskCompletionSource<long> Tcs) : ITokenBudgetCommand;
public sealed record SetBudgetAlertThresholdCmd(double Threshold, TaskCompletionSource Tcs) : ITokenBudgetCommand;
public sealed record ResetBudgetCmd(TaskCompletionSource Tcs) : ITokenBudgetCommand;

/// <summary>
/// Token预算管理器实现 — Actor 化：Consumer 线程独占 _budget/_alertThreshold，消除 AsyncLock。
/// </summary>
[Register(typeof(ITokenBudgetManager), ServiceLifetime.Singleton)]
public partial class TokenBudgetManager : ActorBase<ITokenBudgetCommand, Unit>, ITokenBudgetManager, IAsyncDisposable
{
    private readonly ITelemetryService? _telemetryService;
    private TokenBudget _budget = new();
    private double _alertThreshold = 0.0;
    private int _disposed;

    public event EventHandler<EventArgs>? BudgetAlert;

    public TokenBudgetManager(ITelemetryService? telemetryService = null)
        : base()
    {
        _telemetryService = telemetryService;
        _budget.TotalBudget = 0;
        _budget.UsedTokens = 0;
    }

    private static TaskCompletionSource<T> CreateTcs<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    private static TaskCompletionSource CreateTcs() => new(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task AllocateBudgetAsync(long amount, CancellationToken ct = default)
    {
        var tcs = CreateTcs();
        await SendAsync(new AllocateBudgetCmd(amount, tcs), ct).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    public async Task ConsumeTokensAsync(long amount, string reason, string? toolName = null, CancellationToken ct = default)
    {
        var tcs = CreateTcs();
        await SendAsync(new ConsumeTokensCmd(amount, reason, toolName, tcs), ct).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    public async Task<long> GetRemainingBudgetAsync(CancellationToken ct = default)
    {
        var tcs = CreateTcs<long>();
        await SendAsync(new GetRemainingBudgetCmd(tcs), ct).ConfigureAwait(false);
        return await tcs.Task.ConfigureAwait(false);
    }

    public async Task SetBudgetAlertThresholdAsync(double threshold, CancellationToken ct = default)
    {
        if (threshold < 0 || threshold > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), "[BRN011] 阈值必须在0.0到1.0之间");
        }
        var tcs = CreateTcs();
        await SendAsync(new SetBudgetAlertThresholdCmd(threshold, tcs), ct).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    public async Task ResetBudgetAsync(CancellationToken ct = default)
    {
        var tcs = CreateTcs();
        await SendAsync(new ResetBudgetCmd(tcs), ct).ConfigureAwait(false);
        await tcs.Task.ConfigureAwait(false);
    }

    protected override ValueTask HandleAsync(ITokenBudgetCommand command, CancellationToken ct)
    {
        switch (command)
        {
            case AllocateBudgetCmd alloc:
                _budget.TotalBudget += alloc.Amount;
                alloc.Tcs.TrySetResult();
                break;

            case ConsumeTokensCmd consume:
                _budget.UsedTokens += consume.Amount;

                _telemetryService?.RecordCount("budget.token.consume.count", new() { ["reason"] = consume.Reason }, "count", "Token budget consume count");
                _telemetryService?.RecordHistogram("budget.token.consume.amount", consume.Amount, new() { ["reason"] = consume.Reason }, "tokens", "Token consumption amount");

                if (_alertThreshold > 0 && _budget.TotalBudget > 0)
                {
                    var usagePercentage = (double)_budget.UsedTokens / _budget.TotalBudget;
                    if (usagePercentage >= _alertThreshold)
                    {
                        BudgetAlert?.Invoke(this, EventArgs.Empty);
                    }
                }
                consume.Tcs.TrySetResult();
                break;

            case GetRemainingBudgetCmd getRemaining:
                if (_budget.TotalBudget == 0)
                    getRemaining.Tcs.TrySetResult(long.MaxValue);
                else
                    getRemaining.Tcs.TrySetResult(_budget.RemainingBudget);
                break;

            case SetBudgetAlertThresholdCmd setThreshold:
                _alertThreshold = setThreshold.Threshold;
                setThreshold.Tcs.TrySetResult();
                break;

            case ResetBudgetCmd reset:
                _budget.TotalBudget = 0;
                _budget.UsedTokens = 0;
                reset.Tcs.TrySetResult();
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
