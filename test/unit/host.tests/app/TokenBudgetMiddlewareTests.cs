namespace Host.Tests.App;

/// <summary>
/// TokenBudgetMiddleware 确定性测试 — 验证剩余预算的短路判定纯计算逻辑
/// </summary>
public sealed class TokenBudgetMiddlewareTests {
    [Fact]
    public void ShouldShortCircuit_ZeroRemaining_ShouldReturnTrue() {
        Assert.True(JoinCode.Pipelines.Middlewares.TokenBudgetMiddleware.ShouldShortCircuit(0));
    }

    [Fact]
    public void ShouldShortCircuit_PositiveRemaining_ShouldReturnFalse() {
        Assert.False(JoinCode.Pipelines.Middlewares.TokenBudgetMiddleware.ShouldShortCircuit(1));
    }

    [Fact]
    public void ShouldShortCircuit_NegativeRemaining_ShouldReturnTrue() {
        Assert.True(JoinCode.Pipelines.Middlewares.TokenBudgetMiddleware.ShouldShortCircuit(-1));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    [InlineData(-1, true)]
    [InlineData(100, false)]
    [InlineData(long.MinValue, true)]
    [InlineData(long.MaxValue, false)]
    public void ShouldShortCircuit_BoundaryValues_ShouldMatchExpected(long remaining, bool expected) {
        Assert.Equal(expected, JoinCode.Pipelines.Middlewares.TokenBudgetMiddleware.ShouldShortCircuit(remaining));
    }
}
