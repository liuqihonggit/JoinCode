
namespace Core.Query;

/// <summary>
/// Token预算管理器实现
/// </summary>
[Register]
public partial class TokenBudgetManager : ITokenBudgetManager, IAsyncDisposable
{
    private readonly AsyncLock _lock = new();
    private readonly ITelemetryService? _telemetryService;
    private TokenBudget _budget = new();
    private double _alertThreshold = 0.0;

    public event EventHandler<EventArgs>? BudgetAlert;

    public TokenBudgetManager(ITelemetryService? telemetryService = null)
    {
        _telemetryService = telemetryService;
        _budget.TotalBudget = 0;
        _budget.UsedTokens = 0;
    }

    /// <summary>
    /// 异步分配预算
    /// </summary>
    /// <param name="amount">预算金额</param>
    /// <param name="ct">取消令牌</param>
    public async Task AllocateBudgetAsync(long amount, CancellationToken ct = default)
    {
                using (await _lock.LockAsync(ct).ConfigureAwait(false))
        {
            _budget.TotalBudget += amount;
        }
    }

    /// <summary>
    /// 异步消耗Token
    /// </summary>
    /// <param name="amount">消耗的Token数量</param>
    /// <param name="reason">消耗原因</param>
    /// <param name="toolName">工具名称（可选）</param>
    /// <param name="ct">取消令牌</param>
    public async Task ConsumeTokensAsync(long amount, string reason, string? toolName = null, CancellationToken ct = default)
    {
                using (await _lock.LockAsync(ct).ConfigureAwait(false))
        {
            _budget.UsedTokens += amount;

            _telemetryService?.RecordCount("budget.token.consume.count", new() { ["reason"] = reason }, "count", "Token budget consume count");
            _telemetryService?.RecordHistogram("budget.token.consume.amount", amount, new() { ["reason"] = reason }, "tokens", "Token consumption amount");

            if (_alertThreshold > 0 && _budget.TotalBudget > 0)
            {
                var usagePercentage = (double)_budget.UsedTokens / _budget.TotalBudget;
                if (usagePercentage >= _alertThreshold)
                {
                    BudgetAlert?.Invoke(this, EventArgs.Empty);
                }
            }
        }
    }

    /// <summary>
    /// 异步获取剩余预算
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>剩余预算数量；TotalBudget==0（未分配）时返回 long.MaxValue 表示无限制</returns>
    public async Task<long> GetRemainingBudgetAsync(CancellationToken ct = default)
    {
                using (await _lock.LockAsync(ct).ConfigureAwait(false))
        {
            // 未分配预算时视为无限制，避免阻止所有对话
            if (_budget.TotalBudget == 0)
                return long.MaxValue;
            return _budget.RemainingBudget;
        }
    }

    /// <summary>
    /// 异步设置预算告警阈值
    /// </summary>
    /// <param name="threshold">告警阈值（0.0-1.0，表示预算使用百分比）</param>
    /// <param name="ct">取消令牌</param>
    public async Task SetBudgetAlertThresholdAsync(double threshold, CancellationToken ct = default)
    {
        if (threshold < 0 || threshold > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(threshold), "阈值必须在0.0到1.0之间");
        }

                using (await _lock.LockAsync(ct).ConfigureAwait(false))
        {
            _alertThreshold = threshold;
        }
    }

    /// <summary>
    /// 异步重置预算
    /// </summary>
    /// <param name="ct">取消令牌</param>
    public async Task ResetBudgetAsync(CancellationToken ct = default)
    {
                using (await _lock.LockAsync(ct).ConfigureAwait(false))
        {
            _budget.TotalBudget = 0;
            _budget.UsedTokens = 0;
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lock.Dispose();
    }
}
