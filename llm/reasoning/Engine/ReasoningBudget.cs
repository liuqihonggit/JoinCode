namespace JoinCode.Reasoning.Engine;

/// <summary>
/// 推理预算跟踪 — 轮次与 Token 的使用量与预算上限
/// </summary>
internal sealed class ReasoningBudget
{
    internal int RoundsUsed { get; private set; }
    internal int TokensUsed { get; private set; }
    internal int RoundsBudget { get; private set; }
    internal int TokensBudget { get; private set; }

    internal ReasoningBudget(int maxRounds, int maxTokens)
    {
        RoundsBudget = maxRounds;
        TokensBudget = maxTokens;
    }

    internal void Reset(int maxRounds, int maxTokens)
    {
        RoundsUsed = 0;
        TokensUsed = 0;
        RoundsBudget = maxRounds;
        TokensBudget = maxTokens;
    }

    internal void IncrementRound() => RoundsUsed++;
    internal void RecordTokenUsage(int tokens) => TokensUsed += tokens;
    internal void AddRounds(int rounds) => RoundsBudget += rounds;
    internal void AddTokens(int tokens) => TokensBudget += tokens;

    internal bool IsExhausted => RoundsUsed >= RoundsBudget || TokensUsed >= TokensBudget;
    internal bool IsRoundsExhausted => RoundsUsed >= RoundsBudget;
    internal bool IsTokensExhausted => TokensUsed >= TokensBudget;

    internal BudgetStatus GetStatus() => new()
    {
        RoundsUsed = RoundsUsed,
        RoundsBudget = RoundsBudget,
        TokensUsed = TokensUsed,
        TokensBudget = TokensBudget,
    };
}
