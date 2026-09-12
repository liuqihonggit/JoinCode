namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Token预算管理器接口
/// </summary>
public interface ITokenBudgetManager
{
    /// <summary>
    /// 异步分配预算
    /// </summary>
    /// <param name="amount">预算金额</param>
    /// <param name="ct">取消令牌</param>
    Task AllocateBudgetAsync(long amount, CancellationToken ct = default);

    /// <summary>
    /// 异步消耗Token
    /// </summary>
    /// <param name="amount">消耗的Token数量</param>
    /// <param name="reason">消耗原因</param>
    /// <param name="toolName">工具名称（可选）</param>
    /// <param name="ct">取消令牌</param>
    Task ConsumeTokensAsync(long amount, string reason, string? toolName = null, CancellationToken ct = default);

    /// <summary>
    /// 异步获取剩余预算
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>剩余预算数量</returns>
    Task<long> GetRemainingBudgetAsync(CancellationToken ct = default);

    /// <summary>
    /// 异步设置预算告警阈值
    /// </summary>
    /// <param name="threshold">告警阈值（0.0-1.0，表示预算使用百分比）</param>
    /// <param name="ct">取消令牌</param>
    Task SetBudgetAlertThresholdAsync(double threshold, CancellationToken ct = default);

    /// <summary>
    /// 异步重置预算
    /// </summary>
    /// <param name="ct">取消令牌</param>
    Task ResetBudgetAsync(CancellationToken ct = default);
}
