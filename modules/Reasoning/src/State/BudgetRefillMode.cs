namespace JoinCode.Reasoning.State;

/// <summary>
/// 预算续费方式
/// </summary>
public enum BudgetRefillMode
{
    /// <summary>
    /// 按配置的默认续费量续费
    /// </summary>
    [EnumValue("default")] Default,

    /// <summary>
    /// 仅续费轮次预算
    /// </summary>
    [EnumValue("rounds_only")] RoundsOnly,

    /// <summary>
    /// 仅续费 token 预算
    /// </summary>
    [EnumValue("tokens_only")] TokensOnly,

    /// <summary>
    /// 同时续费轮次和 token 预算
    /// </summary>
    [EnumValue("both")] Both,
}
