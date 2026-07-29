namespace JoinCode.Reasoning.Engine;

/// <summary>
/// 推理预算状态 — 轮次和 token 双预算追踪
/// </summary>
public sealed class BudgetStatus
{
    /// <summary>
    /// 已消耗轮次
    /// </summary>
    public int RoundsUsed { get; init; }

    /// <summary>
    /// 轮次预算上限
    /// </summary>
    public int RoundsBudget { get; init; }

    /// <summary>
    /// 已消耗 token 数
    /// </summary>
    public int TokensUsed { get; init; }

    /// <summary>
    /// Token 预算上限
    /// </summary>
    public int TokensBudget { get; init; }

    /// <summary>
    /// 轮次预算是否耗尽
    /// </summary>
    public bool IsRoundsExhausted => RoundsUsed >= RoundsBudget;

    /// <summary>
    /// Token 预算是否耗尽
    /// </summary>
    public bool IsTokensExhausted => TokensUsed >= TokensBudget;

    /// <summary>
    /// 任一预算耗尽即为触底
    /// </summary>
    public bool IsAnyExhausted => IsRoundsExhausted || IsTokensExhausted;

    /// <summary>
    /// 哪个预算先触底
    /// </summary>
    public BudgetExhaustionCause ExhaustionCause
    {
        get
        {
            if (!IsAnyExhausted) return BudgetExhaustionCause.None;
            if (IsRoundsExhausted && IsTokensExhausted) return BudgetExhaustionCause.Both;
            if (IsRoundsExhausted) return BudgetExhaustionCause.Rounds;
            return BudgetExhaustionCause.Tokens;
        }
    }

    /// <summary>
    /// 剩余轮次
    /// </summary>
    public int RoundsRemaining => Math.Max(0, RoundsBudget - RoundsUsed);

    /// <summary>
    /// 剩余 token
    /// </summary>
    public int TokensRemaining => Math.Max(0, TokensBudget - TokensUsed);
}

/// <summary>
/// 预算耗尽原因
/// </summary>
public enum BudgetExhaustionCause
{
    /// <summary>
    /// 未耗尽
    /// </summary>
    None,

    /// <summary>
    /// 轮次先触底
    /// </summary>
    Rounds,

    /// <summary>
    /// Token 先触底
    /// </summary>
    Tokens,

    /// <summary>
    /// 同时触底
    /// </summary>
    Both,
}
