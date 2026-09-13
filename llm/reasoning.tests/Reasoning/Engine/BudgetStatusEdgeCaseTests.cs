namespace JoinCode.Reasoning.Tests.Engine;

public sealed class BudgetStatusEdgeCaseTests
{
    [Fact]
    public void IsRoundsExhausted_WhenUsedExceedsBudget_ReturnsTrue()
    {
        var status = new BudgetStatus { RoundsUsed = 10, RoundsBudget = 5 };

        Assert.True(status.IsRoundsExhausted);
        Assert.Equal(0, status.RoundsRemaining);
    }

    [Fact]
    public void IsTokensExhausted_WhenUsedExceedsBudget_ReturnsTrue()
    {
        var status = new BudgetStatus { TokensUsed = 2000, TokensBudget = 1000 };

        Assert.True(status.IsTokensExhausted);
        Assert.Equal(0, status.TokensRemaining);
    }

    [Fact]
    public void IsAnyExhausted_WhenNeitherExhausted_ReturnsFalse()
    {
        var status = new BudgetStatus { RoundsUsed = 0, RoundsBudget = 5, TokensUsed = 0, TokensBudget = 1000 };

        Assert.False(status.IsAnyExhausted);
    }

    [Fact]
    public void ExhaustionCause_WhenTokensOnlyExhausted_ReturnsTokens()
    {
        var status = new BudgetStatus { RoundsUsed = 1, RoundsBudget = 5, TokensUsed = 1000, TokensBudget = 1000 };

        Assert.Equal(BudgetExhaustionCause.Tokens, status.ExhaustionCause);
    }

    [Fact]
    public void RoundsRemaining_WhenUnderBudget_ReturnsDifference()
    {
        var status = new BudgetStatus { RoundsUsed = 2, RoundsBudget = 5 };

        Assert.Equal(3, status.RoundsRemaining);
    }

    [Fact]
    public void TokensRemaining_WhenUnderBudget_ReturnsDifference()
    {
        var status = new BudgetStatus { TokensUsed = 3000, TokensBudget = 10000 };

        Assert.Equal(7000, status.TokensRemaining);
    }

    [Fact]
    public void BudgetStatus_DefaultValues_AreZero()
    {
        var status = new BudgetStatus();

        Assert.Equal(0, status.RoundsUsed);
        Assert.Equal(0, status.RoundsBudget);
        Assert.Equal(0, status.TokensUsed);
        Assert.Equal(0, status.TokensBudget);
    }

    [Fact]
    public void BudgetStatus_ZeroBudget_IsExhausted()
    {
        var status = new BudgetStatus { RoundsUsed = 0, RoundsBudget = 0, TokensUsed = 0, TokensBudget = 0 };

        Assert.True(status.IsRoundsExhausted);
        Assert.True(status.IsTokensExhausted);
        Assert.True(status.IsAnyExhausted);
        Assert.Equal(BudgetExhaustionCause.Both, status.ExhaustionCause);
    }
}
