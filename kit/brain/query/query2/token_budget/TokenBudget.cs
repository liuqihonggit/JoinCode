namespace Core.Query;

/// <summary>
/// Token预算信息
/// </summary>
public class TokenBudget
{
    /// <summary>
    /// 总预算
    /// </summary>
    public long TotalBudget { get; set; }

    /// <summary>
    /// 已使用Token
    /// </summary>
    public long UsedTokens { get; set; }

    /// <summary>
    /// 剩余预算
    /// </summary>
    public long RemainingBudget => TotalBudget - UsedTokens;

    /// <summary>
    /// 检查预算是否超出
    /// </summary>
    /// <returns>如果已超出预算返回true，否则返回false</returns>
    public bool IsExceeded()
    {
        return UsedTokens > TotalBudget;
    }
}
