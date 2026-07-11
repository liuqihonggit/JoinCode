
namespace Core.Tests.Query;

public class TokenBudgetManagerTests
{
    [Fact]
    public async Task AllocateBudget_ShouldIncreaseTotalBudget()
    {
        // Arrange
        var manager = new TokenBudgetManager();
        var initialBudget = await manager.GetRemainingBudgetAsync().ConfigureAwait(true);

        // Act
        await manager.AllocateBudgetAsync(1000).ConfigureAwait(true);

        // Assert
        (await manager.GetRemainingBudgetAsync().ConfigureAwait(true)).Should().Be(1000);
    }

    [Fact]
    public async Task ConsumeTokens_ShouldDecreaseRemainingBudget()
    {
        // Arrange
        var manager = new TokenBudgetManager();
        await manager.AllocateBudgetAsync(1000).ConfigureAwait(true);

        // Act
        await manager.ConsumeTokensAsync(100, "Test consumption").ConfigureAwait(true);

        // Assert
        (await manager.GetRemainingBudgetAsync().ConfigureAwait(true)).Should().Be(900);
    }

    [Fact]
    public async Task GetRemainingBudget_WhenNoBudgetAllocated_ShouldReturnUnlimited()
    {
        // Arrange
        var manager = new TokenBudgetManager();

        // Act
        var remaining = await manager.GetRemainingBudgetAsync().ConfigureAwait(true);

        // Assert — 未分配预算时返回 long.MaxValue（无限制），避免阻止所有对话
        remaining.Should().Be(long.MaxValue);
    }

    [Fact]
    public async Task SetBudgetAlertThreshold_ShouldUpdateThreshold()
    {
        // Arrange
        var manager = new TokenBudgetManager();
        await manager.AllocateBudgetAsync(1000).ConfigureAwait(true);
        var alertFired = false;
        manager.BudgetAlert += (s, e) => alertFired = true;

        // Act
        await manager.SetBudgetAlertThresholdAsync(0.5).ConfigureAwait(true);
        await manager.ConsumeTokensAsync(600, "Test consumption").ConfigureAwait(true); // 超过50%

        // Assert
        alertFired.Should().BeTrue();
    }

    [Fact]
    public async Task SetBudgetAlertThreshold_WithInvalidValue_ShouldThrow()
    {
        // Arrange
        var manager = new TokenBudgetManager();

        // Act & Assert
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => manager.SetBudgetAlertThresholdAsync(-0.1)).ConfigureAwait(true);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => manager.SetBudgetAlertThresholdAsync(1.1)).ConfigureAwait(true);
    }

    [Fact]
    public async Task ResetBudget_ShouldClearAllBudget()
    {
        // Arrange
        var manager = new TokenBudgetManager();
        await manager.AllocateBudgetAsync(1000).ConfigureAwait(true);
        await manager.ConsumeTokensAsync(500, "Test consumption").ConfigureAwait(true);

        // Act
        await manager.ResetBudgetAsync().ConfigureAwait(true);

        // Assert — Reset 后 TotalBudget==0（未分配），GetRemainingBudgetAsync 返回 long.MaxValue（无限制）
        (await manager.GetRemainingBudgetAsync().ConfigureAwait(true)).Should().Be(long.MaxValue);
    }

    [Fact]
    public async Task BudgetAlert_ShouldFireWhenThresholdExceeded()
    {
        // Arrange
        var manager = new TokenBudgetManager();
        await manager.AllocateBudgetAsync(1000).ConfigureAwait(true);
        await manager.SetBudgetAlertThresholdAsync(0.8).ConfigureAwait(true);
        var alertFired = false;
        manager.BudgetAlert += (s, e) => alertFired = true;

        // Act
        await manager.ConsumeTokensAsync(850, "Test consumption").ConfigureAwait(true); // 超过80%

        // Assert
        alertFired.Should().BeTrue();
    }

    [Fact]
    public async Task BudgetAlert_ShouldNotFireWhenThresholdNotExceeded()
    {
        // Arrange
        var manager = new TokenBudgetManager();
        await manager.AllocateBudgetAsync(1000).ConfigureAwait(true);
        await manager.SetBudgetAlertThresholdAsync(0.8).ConfigureAwait(true);
        var alertFired = false;
        manager.BudgetAlert += (s, e) => alertFired = true;

        // Act
        await manager.ConsumeTokensAsync(700, "Test consumption").ConfigureAwait(true); // 未超过80%

        // Assert
        alertFired.Should().BeFalse();
    }

    [Fact]
    public async Task MultipleAllocations_ShouldAccumulate()
    {
        // Arrange
        var manager = new TokenBudgetManager();

        // Act
        await manager.AllocateBudgetAsync(500).ConfigureAwait(true);
        await manager.AllocateBudgetAsync(300).ConfigureAwait(true);
        await manager.AllocateBudgetAsync(200).ConfigureAwait(true);

        // Assert
        (await manager.GetRemainingBudgetAsync().ConfigureAwait(true)).Should().Be(1000);
    }

    [Fact]
    public async Task ConsumeTokens_ExceedingBudget_ShouldAllowNegative()
    {
        // Arrange
        var manager = new TokenBudgetManager();
        await manager.AllocateBudgetAsync(100).ConfigureAwait(true);

        // Act
        await manager.ConsumeTokensAsync(150, "Over consumption").ConfigureAwait(true);

        // Assert
        (await manager.GetRemainingBudgetAsync().ConfigureAwait(true)).Should().Be(-50);
    }

    [Fact]
    public async Task BudgetAlert_WithZeroThreshold_ShouldNotFire()
    {
        // Arrange
        var manager = new TokenBudgetManager();
        await manager.AllocateBudgetAsync(1000).ConfigureAwait(true);
        var alertFired = false;
        manager.BudgetAlert += (s, e) => alertFired = true;

        // Act - 不设置阈值，默认为0
        await manager.ConsumeTokensAsync(999, "Test consumption").ConfigureAwait(true);

        // Assert
        alertFired.Should().BeFalse();
    }
}
