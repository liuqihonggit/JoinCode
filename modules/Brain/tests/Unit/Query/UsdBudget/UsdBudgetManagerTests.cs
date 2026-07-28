namespace Core.Tests.Query.UsdBudget;

public class UsdBudgetManagerTests : IAsyncDisposable
{
    private readonly Mock<JoinCode.Abstractions.Interfaces.ICostTracker> _costTrackerMock = new();
    private readonly QueryEngineConfig _config = new() { MaxUsdBudget = 10.0m, UsdAlertThreshold = 0.8 };
    private readonly UsdBudgetManager _manager;

    public UsdBudgetManagerTests()
    {
        var optionsMock = new Mock<IOptions<QueryEngineConfig>>();
        optionsMock.SetupGet(o => o.Value).Returns(_config);
        _manager = new UsdBudgetManager(_costTrackerMock.Object, optionsMock.Object, NullLogger<UsdBudgetManager>.Instance);
    }

    [Fact]
    public async Task IsBudgetExceededAsync_UnderBudget_ShouldReturnFalse()
    {
        var result = await _manager.IsBudgetExceededAsync().ConfigureAwait(true);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task IsBudgetExceededAsync_OverBudget_ShouldReturnTrue()
    {
        await _manager.RecordCostAsync(8.0m, "api call").ConfigureAwait(true);
        await _manager.RecordCostAsync(3.0m, "another call").ConfigureAwait(true);

        var result = await _manager.IsBudgetExceededAsync().ConfigureAwait(true);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task RecordCostAsync_ShouldAccumulateCosts()
    {
        await _manager.RecordCostAsync(2.0m, "first call").ConfigureAwait(true);
        await _manager.RecordCostAsync(3.0m, "second call").ConfigureAwait(true);

        var status = await _manager.GetBudgetStatusAsync().ConfigureAwait(true);

        status.TotalUsed.Should().Be(5.0m);
        status.UsagePercentage.Should().BeApproximately(0.5, 0.001);
    }

    [Fact]
    public async Task BudgetAlert_AtThreshold_ShouldFireEvent()
    {
        UsdBudgetAlertEventArgs? alertArgs = null;
        _manager.BudgetAlert += (_, args) => alertArgs = args;

        await _manager.RecordCostAsync(8.5m, "expensive call").ConfigureAwait(true);

        alertArgs.Should().NotBeNull();
        alertArgs!.UsagePercentage.Should().BeGreaterThanOrEqualTo(0.8);
        alertArgs.MaxBudget.Should().Be(10.0m);
        alertArgs.Message.Should().Contain("alert");
    }

    [Fact]
    public async Task BudgetAlert_BelowThreshold_ShouldNotFireEvent()
    {
        var eventFired = false;
        _manager.BudgetAlert += (_, _) => eventFired = true;

        await _manager.RecordCostAsync(1.0m, "cheap call").ConfigureAwait(true);

        eventFired.Should().BeFalse();
    }

    [Fact]
    public async Task IsBudgetExceededAsync_NoBudgetConfigured_ShouldReturnFalse()
    {
        var configNoBudget = new QueryEngineConfig { MaxUsdBudget = null };
        var optionsMock = new Mock<IOptions<QueryEngineConfig>>();
        optionsMock.SetupGet(o => o.Value).Returns(configNoBudget);
        await using var managerNoBudget = new UsdBudgetManager(_costTrackerMock.Object, optionsMock.Object);

        var result = await managerNoBudget.IsBudgetExceededAsync().ConfigureAwait(true);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task RecordCostAsync_NoBudgetConfigured_ShouldNotRecord()
    {
        var configNoBudget = new QueryEngineConfig { MaxUsdBudget = null };
        var optionsMock = new Mock<IOptions<QueryEngineConfig>>();
        optionsMock.SetupGet(o => o.Value).Returns(configNoBudget);
        await using var managerNoBudget = new UsdBudgetManager(_costTrackerMock.Object, optionsMock.Object);

        await managerNoBudget.RecordCostAsync(5.0m, "call").ConfigureAwait(true);

        var status = await managerNoBudget.GetBudgetStatusAsync().ConfigureAwait(true);
        status.TotalUsed.Should().Be(0m);
    }

    [Fact]
    public async Task GetBudgetStatusAsync_ShouldReturnCorrectStatus()
    {
        await _manager.RecordCostAsync(3.0m, "call").ConfigureAwait(true);

        var status = await _manager.GetBudgetStatusAsync().ConfigureAwait(true);

        status.MaxBudget.Should().Be(10.0m);
        status.TotalUsed.Should().Be(3.0m);
        status.Remaining.Should().Be(7.0m);
        status.IsExceeded.Should().BeFalse();
    }

    [Fact]
    public async Task RecordCostAsync_NullReason_ShouldThrowArgumentNullException()
    {
        var act = async () => await _manager.RecordCostAsync(1.0m, null!).ConfigureAwait(true);

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task BudgetAlert_ShouldFireOnlyOnce()
    {
        var alertCount = 0;
        _manager.BudgetAlert += (_, _) => alertCount++;

        await _manager.RecordCostAsync(9.0m, "first expensive call").ConfigureAwait(true);
        await _manager.RecordCostAsync(1.0m, "second call").ConfigureAwait(true);

        alertCount.Should().Be(1);
    }

    [Fact]
    public async Task IsBudgetExceededAsync_ZeroBudget_ShouldReturnFalse()
    {
        var configZero = new QueryEngineConfig { MaxUsdBudget = 0m };
        var optionsMock = new Mock<IOptions<QueryEngineConfig>>();
        optionsMock.SetupGet(o => o.Value).Returns(configZero);
        await using var managerZero = new UsdBudgetManager(_costTrackerMock.Object, optionsMock.Object);

        var result = await managerZero.IsBudgetExceededAsync().ConfigureAwait(true);

        result.Should().BeFalse();
    }

    public async ValueTask DisposeAsync()
    {
        await _manager.DisposeAsync().ConfigureAwait(true);
    }
}
