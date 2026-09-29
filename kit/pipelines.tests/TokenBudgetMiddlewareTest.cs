namespace Pipelines.Tests;

/// <summary>
/// TokenBudgetMiddleware 确定性单元测试 — mock ITokenBudgetManager 控制剩余预算，
/// 验证 IsDryRun 跳过检查、预算耗尽短路、预算充足转发。
/// 无 IO/时序依赖。
/// </summary>
public sealed class TokenBudgetMiddlewareTest {
    private static TokenBudgetMiddleware NewMiddleware(Mock<ITokenBudgetManager> budgetMock)
        => new(budgetMock.Object, NullLogger<TokenBudgetMiddleware>.Instance);

    private static Mock<ITokenBudgetManager> BudgetMockReturning(long remaining) {
        var mock = new Mock<ITokenBudgetManager>();
        mock.Setup(b => b.GetRemainingBudgetAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(remaining);
        return mock;
    }

    // === InvokeAsync: IsDryRun 分支 ===

    [Fact]
    public async Task InvokeAsync_IsDryRunTrue_PassesThroughWithoutCheckingBudget() {
        var budgetMock = new Mock<ITokenBudgetManager>();
        var mw = NewMiddleware(budgetMock);
        var ctx = TestHelpers.NewContext(dryRun: true);
        var events = new[] { ChatStreamEvent.Text("preview"), ChatStreamEvent.Done() };

        var result = await TestHelpers.CollectAsync(
            mw.InvokeAsync(ctx, (_, ct) => TestHelpers.EventsAsync(events, ct), default));

        result.Should().HaveCount(2);
        result[0].Content.Should().Be("preview");
        budgetMock.Verify(b => b.GetRemainingBudgetAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task InvokeAsync_IsDryRunFalse_RemainingPositive_PassesThroughNext() {
        var budgetMock = BudgetMockReturning(1000L);
        var mw = NewMiddleware(budgetMock);
        var ctx = TestHelpers.NewContext(dryRun: false);
        var events = new[] { ChatStreamEvent.Text("response"), ChatStreamEvent.Done() };

        var result = await TestHelpers.CollectAsync(
            mw.InvokeAsync(ctx, (_, ct) => TestHelpers.EventsAsync(events, ct), default));

        result.Should().HaveCount(2);
        result[0].Content.Should().Be("response");
        budgetMock.Verify(b => b.GetRemainingBudgetAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task InvokeAsync_IsDryRunFalse_RemainingZero_ShortCircuitsWithBudgetExhausted() {
        var budgetMock = BudgetMockReturning(0L);
        var mw = NewMiddleware(budgetMock);
        var ctx = TestHelpers.NewContext(dryRun: false);
        var nextEvents = new[] { ChatStreamEvent.Text("should-not-appear") };

        var result = await TestHelpers.CollectAsync(
            mw.InvokeAsync(ctx, (_, ct) => TestHelpers.EventsAsync(nextEvents, ct), default));

        result.Should().HaveCount(1);
        result[0].Type.Should().Be(ChatStreamEventType.Content);
        result[0].Content.Should().Contain("[Token 预算已耗尽]");
    }

    [Fact]
    public async Task InvokeAsync_IsDryRunFalse_RemainingNegative_ShortCircuitsWithBudgetExhausted() {
        var budgetMock = BudgetMockReturning(-50L);
        var mw = NewMiddleware(budgetMock);
        var ctx = TestHelpers.NewContext(dryRun: false);

        var result = await TestHelpers.CollectAsync(
            mw.InvokeAsync(ctx, (_, ct) => TestHelpers.EventsAsync([], ct), default));

        result.Should().HaveCount(1);
        result[0].Content.Should().Contain("[Token 预算已耗尽]");
    }

    // === ShouldShortCircuit: 纯函数确定性测试 ===

    [Theory]
    [InlineData(0L, true)]
    [InlineData(-1L, true)]
    [InlineData(-1000L, true)]
    [InlineData(1L, false)]
    [InlineData(1000L, false)]
    public void ShouldShortCircuit_ReturnsTrueForNonPositiveAndFalseForPositive(long remaining, bool expected) {
        TokenBudgetMiddleware.ShouldShortCircuit(remaining).Should().Be(expected);
    }
}
