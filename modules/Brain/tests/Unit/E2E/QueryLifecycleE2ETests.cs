namespace Core.Tests.Services.E2E;

public sealed class QueryLifecycleE2ETests : IAsyncDisposable
{
    private readonly QueryStateTransitions _transitions;
    private readonly QueryStopHookManager _stopHooks;
    private readonly DiminishingReturnsDetector _diminishingReturns;
    private readonly UsdBudgetManager _budgetManager;
    private readonly HistorySnipService _snipService;

    public QueryLifecycleE2ETests()
    {
        _transitions = new QueryStateTransitions();
        _stopHooks = new QueryStopHookManager();
        _diminishingReturns = new DiminishingReturnsDetector();
        _snipService = new HistorySnipService();

        var costTracker = new Mock<JoinCode.Abstractions.Interfaces.ICostTracker>();
        costTracker.Setup(c => c.GetTotalStatistics()).Returns(new JoinCode.Abstractions.Interfaces.CostStatistics());
        var config = Options.Create(new QueryEngineConfig
        {
            MaxUsdBudget = 10.0m,
            UsdAlertThreshold = 0.8
        });
        _budgetManager = new UsdBudgetManager(costTracker.Object, config);
    }

    public async ValueTask DisposeAsync()
    {
        await _budgetManager.DisposeAsync().ConfigureAwait(true);
    }

    [Fact]
    public void FullQueryLifecycle_StateTransitions_ShouldFollowValidPath()
    {
        _transitions.CurrentState.Should().Be(QueryState.Idle);

        _transitions.TransitionTo(QueryState.Initializing);
        _transitions.CurrentState.Should().Be(QueryState.Initializing);

        _transitions.TransitionTo(QueryState.Running);
        _transitions.CurrentState.Should().Be(QueryState.Running);

        _transitions.TransitionTo(QueryState.WaitingForTool);
        _transitions.CurrentState.Should().Be(QueryState.WaitingForTool);

        _transitions.TransitionTo(QueryState.ExecutingTool);
        _transitions.CurrentState.Should().Be(QueryState.ExecutingTool);

        _transitions.TransitionTo(QueryState.Running);
        _transitions.TransitionTo(QueryState.Stopping);
        _transitions.TransitionTo(QueryState.Completed);
        _transitions.CurrentState.Should().Be(QueryState.Completed);

        _transitions.TransitionTo(QueryState.Idle);
        _transitions.CurrentState.Should().Be(QueryState.Idle);
    }

    [Fact]
    public async Task QueryLifecycle_WithStopHook_ShouldExecuteHookAndReturnStop()
    {
        _transitions.TransitionTo(QueryState.Initializing);
        _transitions.TransitionTo(QueryState.Running);

        var stopHook = new Mock<IQueryStopHook>();
        stopHook.Setup(h => h.Name).Returns("e2e-stop-hook");
        stopHook.Setup(h => h.Priority).Returns(1);
        stopHook.Setup(h => h.OnStopAsync(It.IsAny<StopHookContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StopHookResult.Continue("Continue processing"));

        _stopHooks.RegisterStopHook(stopHook.Object);

        var result = await _stopHooks.ExecuteStopHooksAsync("session-1", "budget exceeded").ConfigureAwait(true);

        result.ShouldStop.Should().BeTrue();
        stopHook.Verify(h => h.OnStopAsync(It.IsAny<StopHookContext>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task QueryLifecycle_UsdBudgetTracking_ShouldDetectExceeded()
    {
        (await _budgetManager.IsBudgetExceededAsync().ConfigureAwait(true)).Should().BeFalse();

        await _budgetManager.RecordCostAsync(8.0m, "LLM call 1").ConfigureAwait(true);
        (await _budgetManager.IsBudgetExceededAsync().ConfigureAwait(true)).Should().BeFalse();

        await _budgetManager.RecordCostAsync(3.0m, "LLM call 2").ConfigureAwait(true);
        (await _budgetManager.IsBudgetExceededAsync().ConfigureAwait(true)).Should().BeTrue();

        var status = await _budgetManager.GetBudgetStatusAsync().ConfigureAwait(true);
        status.TotalUsed.Should().Be(11.0m);
        status.UsagePercentage.Should().BeApproximately(1.0, 0.01);
        status.IsExceeded.Should().BeTrue();
    }

    [Fact]
    public void QueryLifecycle_DiminishingReturns_ShouldDetectAfterConsecutiveLowValues()
    {
        var highConsumptions = new List<TokenConsumption>
        {
            new() { Amount = 1000 },
            new() { Amount = 900 },
            new() { Amount = 800 }
        };

        var result1 = _diminishingReturns.CheckDiminishingReturns(highConsumptions);
        result1.IsDiminishing.Should().BeFalse();

        var veryLowConsumptions = new List<TokenConsumption>
        {
            new() { Amount = 1000 },
            new() { Amount = 10 },
            new() { Amount = 1 }
        };

        var result2 = _diminishingReturns.CheckDiminishingReturns(veryLowConsumptions);
        result2.IsDiminishing.Should().BeFalse();

        var result3 = _diminishingReturns.CheckDiminishingReturns(veryLowConsumptions);
        result3.IsDiminishing.Should().BeFalse();

        var result4 = _diminishingReturns.CheckDiminishingReturns(veryLowConsumptions);
        result4.IsDiminishing.Should().BeTrue();
    }

    [Fact]
    public async Task QueryLifecycle_HistorySnip_ShouldReduceContext()
    {
        var history = new MessageList();
        history.AddSystemMessage("System prompt");
        for (var i = 0; i < 20; i++)
        {
            history.AddUserMessage($"User message {i} with some content to make it longer");
            history.AddAssistantMessage($"Assistant response {i} with detailed output to increase token count");
        }

        var originalCount = history.Count;

        var result = await _snipService.SnipByMessageCountAsync(history, 10).ConfigureAwait(true);

        result.MessagesRemoved.Should().BeGreaterThan(0);
        history.Count.Should().BeLessThan(originalCount);
        history.Count.Should().BeLessThanOrEqualTo(10 + 1);
    }

    [Fact]
    public void QueryLifecycle_CompactionTransition_ShouldBeValid()
    {
        _transitions.TransitionTo(QueryState.Initializing);
        _transitions.TransitionTo(QueryState.Running);

        var canCompact = _transitions.CanTransitionTo(QueryState.Running, QueryState.Compacting);
        canCompact.Should().BeTrue();

        _transitions.TransitionTo(QueryState.Compacting);
        _transitions.CurrentState.Should().Be(QueryState.Compacting);

        var canResume = _transitions.CanTransitionTo(QueryState.Compacting, QueryState.Running);
        canResume.Should().BeTrue();

        _transitions.TransitionTo(QueryState.Running);
        _transitions.CurrentState.Should().Be(QueryState.Running);
    }

    [Fact]
    public async Task QueryLifecycle_BudgetAlert_ShouldFireAtThreshold()
    {
        var alertFired = false;
        _budgetManager.BudgetAlert += (_, args) =>
        {
            alertFired = true;
            args.UsagePercentage.Should().BeGreaterThanOrEqualTo(0.8);
        };

        await _budgetManager.RecordCostAsync(8.5m, "expensive call").ConfigureAwait(true);

        alertFired.Should().BeTrue();
    }
}
