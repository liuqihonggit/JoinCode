
namespace Core.Query;

/// <summary>
/// Token预算管理命令 — Actor 消息类型
/// </summary>
public interface ITokenBudgetCommand;

/// <summary>
/// 分配预算命令
/// </summary>
/// <param name="Amount">分配额度</param>
/// <param name="Tcs">结果回源</param>
public sealed record AllocateBudgetCmd(long Amount, TaskCompletionSource Tcs) : ITokenBudgetCommand;

/// <summary>
/// 消耗 Token 命令
/// </summary>
/// <param name="Amount">消耗数量</param>
/// <param name="Reason">消耗原因</param>
/// <param name="ToolName">工具名称（可选）</param>
/// <param name="Tcs">结果回源</param>
public sealed record ConsumeTokensCmd(long Amount, string Reason, string? ToolName, TaskCompletionSource Tcs) : ITokenBudgetCommand;

/// <summary>
/// 查询剩余预算命令
/// </summary>
/// <param name="Tcs">结果回源</param>
public sealed record GetRemainingBudgetCmd(TaskCompletionSource<long> Tcs) : ITokenBudgetCommand;

/// <summary>
/// 设置预算告警阈值命令
/// </summary>
/// <param name="Threshold">阈值（0.0-1.0）</param>
/// <param name="Tcs">结果回源</param>
public sealed record SetBudgetAlertThresholdCmd(double Threshold, TaskCompletionSource Tcs) : ITokenBudgetCommand;

/// <summary>
/// 重置预算命令
/// </summary>
/// <param name="Tcs">结果回源</param>
public sealed record ResetBudgetCmd(TaskCompletionSource Tcs) : ITokenBudgetCommand;

/// <summary>
/// Token预算管理器实现 — Actor 化：Consumer 线程独占 _budget/_alertThreshold，消除 AsyncLock。
/// </summary>
[Register(typeof(ITokenBudgetManager), ServiceLifetime.Singleton)]
public partial class TokenBudgetManager : ActorBase<ITokenBudgetCommand, Unit>, ITokenBudgetManager, IAsyncDisposable {
    private readonly ITelemetryService? _telemetryService;
    private readonly TokenBudget _budget = new();
    private double _alertThreshold = 0.0;
    private int _disposed;

    /// <summary>
    /// 预算告警事件 — 使用率达到阈值时触发
    /// </summary>
    public event EventHandler<EventArgs>? BudgetAlert;

    /// <summary>
    /// 构造函数 — 注入遥测服务（可选）
    /// </summary>
    /// <param name="telemetryService">遥测服务</param>
    public TokenBudgetManager(ITelemetryService? telemetryService = null)
        : base() {
        _telemetryService = telemetryService;
        _budget.TotalBudget = 0;
        _budget.UsedTokens = 0;
    }


    /// <summary>
    /// 分配预算额度
    /// </summary>
    /// <param name="amount">分配额度</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task AllocateBudgetAsync(long amount, CancellationToken ct = default) {
        var tcs = TcsFactory.Create();
        await SendAsync(new AllocateBudgetCmd(amount, tcs), ct).ConfigureAwait(false);
        await AskAwait(tcs, ct);
    }

    /// <summary>
    /// 消耗 Token 预算
    /// </summary>
    /// <param name="amount">消耗数量</param>
    /// <param name="reason">消耗原因</param>
    /// <param name="toolName">工具名称（可选）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task ConsumeTokensAsync(long amount, string reason, string? toolName = null, CancellationToken ct = default) {
        var tcs = TcsFactory.Create();
        await SendAsync(new ConsumeTokensCmd(amount, reason, toolName, tcs), ct).ConfigureAwait(false);
        await AskAwait(tcs, ct);
    }

    /// <summary>
    /// 获取剩余预算
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>剩余 Token 数；总预算为 0 时返回 long.MaxValue</returns>
    public async Task<long> GetRemainingBudgetAsync(CancellationToken ct = default) {
        var tcs = TcsFactory.Create<long>();
        await SendAsync(new GetRemainingBudgetCmd(tcs), ct).ConfigureAwait(false);
        return await AskAwait(tcs, ct);
    }

    /// <summary>
    /// 设置预算告警阈值
    /// </summary>
    /// <param name="threshold">阈值（0.0-1.0）</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task SetBudgetAlertThresholdAsync(double threshold, CancellationToken ct = default) {
        if (threshold < 0 || threshold > 1) {
            throw new ArgumentOutOfRangeException(nameof(threshold), "[BRN011] 阈值必须在0.0到1.0之间");
        }
        var tcs = TcsFactory.Create();
        await SendAsync(new SetBudgetAlertThresholdCmd(threshold, tcs), ct).ConfigureAwait(false);
        await AskAwait(tcs, ct);
    }

    /// <summary>
    /// 重置预算 — 清零总预算和已用 Token
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的任务</returns>
    public async Task ResetBudgetAsync(CancellationToken ct = default) {
        var tcs = TcsFactory.Create();
        await SendAsync(new ResetBudgetCmd(tcs), ct).ConfigureAwait(false);
        await AskAwait(tcs, ct);
    }

    /// <summary>
    /// 处理 Actor 消息 — 在 Consumer 线程独占执行
    /// </summary>
    /// <param name="command">命令消息</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步操作的值任务</returns>
    protected override ValueTask HandleAsync(ITokenBudgetCommand command, CancellationToken ct) {
        switch (command) {
            case AllocateBudgetCmd alloc:
            _budget.TotalBudget += alloc.Amount;
            alloc.Tcs.TrySetResult();
            break;

            case ConsumeTokensCmd consume:
            _budget.UsedTokens += consume.Amount;

            _telemetryService?.RecordCount("budget.token.consume.count", new() { ["reason"] = consume.Reason }, "count", "Token budget consume count");
            _telemetryService?.RecordHistogram("budget.token.consume.amount", consume.Amount, new() { ["reason"] = consume.Reason }, "tokens", "Token consumption amount");

            if (_alertThreshold > 0 && _budget.TotalBudget > 0) {
                var usagePercentage = (double)_budget.UsedTokens / _budget.TotalBudget;
                if (usagePercentage >= _alertThreshold) {
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

    /// <summary>
    /// Consumer 线程异常回调 — 空实现，异常由 Actor 基类统一处理
    /// </summary>
    /// <param name="ex">异常对象</param>
    protected override void OnConsumerError(Exception ex) {
    }

    /// <summary>
    /// 异步释放资源
    /// </summary>
    /// <returns>表示异步操作的值任务</returns>
    public override async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await base.DisposeAsync().ConfigureAwait(false);
    }
}