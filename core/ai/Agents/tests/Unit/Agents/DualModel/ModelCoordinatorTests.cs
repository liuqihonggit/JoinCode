namespace Sync.Tests.Unit.Agents.DualModel;

public sealed class ModelCoordinatorTests
{
    private static readonly string DefaultFastModelId = ModelConfigLoader.GetDefaultFastModelId("openai");
    private static readonly string DefaultModelId = ModelConfigLoader.GetDefaultModelId("openai");

    [Fact]
    public async Task PlanAsync_Success_ReturnsPlanResult()
    {
        var queryEngine = CreateMockQueryEngine("1. Read the file\n2. Fix the bug\n3. Add tests");
        var coordinator = new ModelCoordinator(
            queryEngine, DefaultFastModelId, DefaultModelId);

        var result = await coordinator.PlanAsync("Fix the login bug", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        Assert.Contains("Fix the bug", result.Plan);
        Assert.False(result.IsNoOp);
    }

    [Fact]
    public async Task PlanAsync_NoOpPlan_ReturnsNoOpResult()
    {
        var queryEngine = CreateMockQueryEngine("no changes needed, already implemented");
        var coordinator = new ModelCoordinator(
            queryEngine, DefaultFastModelId, DefaultModelId);

        var result = await coordinator.PlanAsync("Check if login works", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        Assert.True(result.IsNoOp);
    }

    [Fact]
    public async Task ExecuteAsync_Success_ReturnsExecutionResult()
    {
        var queryEngine = CreateMockQueryEngine("Bug fixed and tests added");
        var coordinator = new ModelCoordinator(
            queryEngine, DefaultFastModelId, DefaultModelId);

        var result = await coordinator.ExecuteAsync("Fix the login bug", "1. Fix the bug\n2. Add tests", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        Assert.Contains("Bug fixed", result.Output);
    }

    [Fact]
    public async Task PlanAndExecuteAsync_Success_ReturnsCoordinationResult()
    {
        var callCount = 0;
        var queryEngine = CreateMockQueryEngine(_ =>
        {
            callCount++;
            return callCount == 1 ? "1. Fix the bug" : "Bug fixed";
        });

        var coordinator = new ModelCoordinator(
            queryEngine, DefaultFastModelId, DefaultModelId);

        var result = await coordinator.PlanAndExecuteAsync("Fix the login bug", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        Assert.True(result.Plan.Succeeded);
        Assert.NotNull(result.Execution);
        Assert.True(result.Execution.Succeeded);
    }

    [Fact]
    public async Task PlanAndExecuteAsync_NoOpPlan_SkipsExecution()
    {
        var queryEngine = CreateMockQueryEngine("no changes needed");
        var coordinator = new ModelCoordinator(
            queryEngine, DefaultFastModelId, DefaultModelId);

        var result = await coordinator.PlanAndExecuteAsync("Check if login works", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        Assert.True(result.Plan.IsNoOp);
        Assert.Null(result.Execution);
    }

    [Fact]
    public async Task PlanAndExecuteAsync_WithShouldPlan_SkipsPlanning()
    {
        var callCount = 0;
        var queryEngine = CreateMockQueryEngine(_ =>
        {
            callCount++;
            return "Direct response";
        });

        var coordinator = new ModelCoordinator(
            queryEngine, "gpt-4o-mini", "gpt-4o",
            shouldPlan: input => false);

        var result = await coordinator.PlanAndExecuteAsync("Hello", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.Succeeded);
        Assert.False(result.Plan.IsNoOp);
    }

    [Fact]
    public void ResetPlannerSession_ClearsSession()
    {
        var queryEngine = CreateMockQueryEngine("plan");
        var coordinator = new ModelCoordinator(
            queryEngine, DefaultFastModelId, DefaultModelId);

        coordinator.ResetPlannerSession();
    }

    [Fact]
    public void DefaultPlannerTools_ContainsReadOnlyTools()
    {
        var tools = ModelCoordinator.DefaultPlannerTools();
        Assert.Contains("read_file", tools);
        Assert.Contains("search_files", tools);
        Assert.Contains("glob", tools);
        Assert.DoesNotContain("write_file", tools);
        Assert.DoesNotContain("edit_file", tools);
    }

    private static IQueryEngine CreateMockQueryEngine(string response)
    {
        var mock = new Mock<IQueryEngine>();
        mock.Setup(e => e.QueryAsync(It.IsAny<string>(), It.IsAny<MessageList>(), It.IsAny<QueryOptions?>(), It.IsAny<CancellationToken>()))
            .Returns((string input, MessageList history, QueryOptions? options, CancellationToken ct) =>
                EmitChunksAsync(response, ct));
        mock.Setup(e => e.QueryAsync(It.IsAny<string>(), It.IsAny<MessageList>(), It.IsAny<CancellationToken>()))
            .Returns((string input, MessageList history, CancellationToken ct) =>
                EmitChunksAsync(response, ct));
        return mock.Object;
    }

    private static IQueryEngine CreateMockQueryEngine(Func<string, string> responseFunc)
    {
        var mock = new Mock<IQueryEngine>();
        mock.Setup(e => e.QueryAsync(It.IsAny<string>(), It.IsAny<MessageList>(), It.IsAny<QueryOptions?>(), It.IsAny<CancellationToken>()))
            .Returns((string input, MessageList history, QueryOptions? options, CancellationToken ct) =>
                EmitChunksAsync(responseFunc(input), ct));
        mock.Setup(e => e.QueryAsync(It.IsAny<string>(), It.IsAny<MessageList>(), It.IsAny<CancellationToken>()))
            .Returns((string input, MessageList history, CancellationToken ct) =>
                EmitChunksAsync(responseFunc(input), ct));
        return mock.Object;
    }

    private static async IAsyncEnumerable<QueryStreamChunk> EmitChunksAsync(
        string response,
        [EnumeratorCancellation] CancellationToken ct)
    {
        yield return new QueryStreamChunk
        {
            Type = AgentStreamChunkType.Content,
            Content = response
        };
        yield return new QueryStreamChunk
        {
            Type = AgentStreamChunkType.Complete,
            Content = response
        };
    }
}
