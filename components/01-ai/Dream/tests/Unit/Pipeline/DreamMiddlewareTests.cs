namespace Dream.Tests.Pipeline;

using JoinCode.Dream.Pipeline;

public sealed class DreamMiddlewareTests
{
    private readonly Mock<IChatCompletionClient> _chatClient;
    private readonly Mock<ISessionScanner> _sessionScanner;
    private readonly InMemoryDreamTaskRegistry _taskRegistry;
    private readonly AutoDreamConfig _config;

    public DreamMiddlewareTests()
    {
        _chatClient = new Mock<IChatCompletionClient>();
        _sessionScanner = new Mock<ISessionScanner>();
        _taskRegistry = new InMemoryDreamTaskRegistry();
        _config = new AutoDreamConfig { Enabled = true, MinHours = 1, MinSessions = 1 };
    }

    // === DreamGateCheckMiddleware ===

    [Fact]
    public async Task Gate_Disabled_SetsSkippedResult()
    {
        var config = new AutoDreamConfig { Enabled = false, MinHours = 1, MinSessions = 1 };
        var mw = new DreamGateCheckMiddleware(_sessionScanner.Object, config, NullLogger<DreamGateCheckMiddleware>.Instance);
        var ctx = new DreamContext { Request = new DreamRequest() };

        await mw.InvokeAsync(ctx, (c, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        ctx.Result.Should().NotBeNull();
        ctx.Result!.IsSkipped.Should().BeTrue();
    }

    [Fact]
    public async Task Gate_Force_SkipsCheck()
    {
        var config = new AutoDreamConfig { Enabled = false, MinHours = 1, MinSessions = 1 };
        var mw = new DreamGateCheckMiddleware(_sessionScanner.Object, config, NullLogger<DreamGateCheckMiddleware>.Instance);
        var ctx = new DreamContext { Request = new DreamRequest(Force: true) };

        await mw.InvokeAsync(ctx, (c, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        ctx.Result.Should().BeNull();
        ctx.GateChecked.Should().BeTrue();
    }

    [Fact]
    public async Task Gate_InsufficientSessions_SetsSkippedResult()
    {
        _sessionScanner.Setup(x => x.ListSessionsTouchedSinceAsync(It.IsAny<long>(), default))
            .ReturnsAsync([]);

        var mw = new DreamGateCheckMiddleware(_sessionScanner.Object, _config, NullLogger<DreamGateCheckMiddleware>.Instance);
        var ctx = new DreamContext { Request = new DreamRequest() };

        await mw.InvokeAsync(ctx, (c, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        ctx.Result.Should().NotBeNull();
        ctx.Result!.IsSkipped.Should().BeTrue();
    }

    // === DreamSessionScanMiddleware ===

    [Fact]
    public async Task Scan_UserProvidedSessions_UsesThem()
    {
        var mw = new DreamSessionScanMiddleware(_sessionScanner.Object, _config, NullLogger<DreamSessionScanMiddleware>.Instance);
        var ctx = new DreamContext { Request = new DreamRequest(SessionIds: ["s1", "s2"]) };

        await mw.InvokeAsync(ctx, (c, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        ctx.SessionIds.Should().Equal("s1", "s2");
        ctx.SessionsScanned.Should().BeTrue();
    }

    [Fact]
    public async Task Scan_NoSessions_SetsSkippedResult()
    {
        _sessionScanner.Setup(x => x.ListSessionsTouchedSinceAsync(It.IsAny<long>(), default))
            .ReturnsAsync([]);

        var mw = new DreamSessionScanMiddleware(_sessionScanner.Object, _config, NullLogger<DreamSessionScanMiddleware>.Instance);
        var ctx = new DreamContext { Request = new DreamRequest() };

        await mw.InvokeAsync(ctx, (c, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        ctx.Result.Should().NotBeNull();
        ctx.Result!.IsSkipped.Should().BeTrue();
    }

    [Fact]
    public async Task Scan_AutoScanWithSessions_SetsSessionIds()
    {
        _sessionScanner.Setup(x => x.ListSessionsTouchedSinceAsync(It.IsAny<long>(), default))
            .ReturnsAsync(["s1", "s2", "s3"]);

        var mw = new DreamSessionScanMiddleware(_sessionScanner.Object, _config, NullLogger<DreamSessionScanMiddleware>.Instance);
        var ctx = new DreamContext { Request = new DreamRequest() };

        await mw.InvokeAsync(ctx, (c, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        ctx.SessionIds.Should().Equal("s1", "s2", "s3");
        ctx.SessionsScanned.Should().BeTrue();
    }

    // === DreamTaskRegisterMiddleware ===

    [Fact]
    public async Task Register_RegistersTask_SetsTaskId()
    {
        var mw = new DreamTaskRegisterMiddleware(_taskRegistry, _config);
        var ctx = new DreamContext { Request = new DreamRequest(), SessionIds = ["s1"] };

        await mw.InvokeAsync(ctx, (c, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        ctx.TaskId.Should().NotBeNullOrEmpty();
        ctx.TaskRegistered.Should().BeTrue();
    }

    // === DreamPromptBuildMiddleware ===

    [Fact]
    public async Task PromptBuild_SetsPrompts()
    {
        var mw = new DreamPromptBuildMiddleware();
        var ctx = new DreamContext { Request = new DreamRequest(), SessionIds = ["s1"] };

        await mw.InvokeAsync(ctx, (c, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        ctx.SystemPrompt.Should().NotBeNullOrEmpty();
        ctx.UserPrompt.Should().NotBeNullOrEmpty();
        ctx.PromptBuilt.Should().BeTrue();
    }

    // === DreamLlmConsolidateMiddleware ===

    [Fact]
    public async Task LlmConsolidate_SetsResult()
    {
        _chatClient.Setup(x => x.GetCompletionAsync(It.IsAny<MessageList>(), default))
            .ReturnsAsync("consolidated memory");

        var mw = new DreamLlmConsolidateMiddleware(_chatClient.Object);
        var ctx = new DreamContext
        {
            Request = new DreamRequest(),
            SystemPrompt = "system",
            UserPrompt = "user",
        };

        await mw.InvokeAsync(ctx, (c, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        ctx.ConsolidationResult.Should().Be("consolidated memory");
        ctx.LlmCompleted.Should().BeTrue();
    }

    // === DreamRecordTurnMiddleware ===

    [Fact]
    public async Task RecordTurn_RecordsAndCompletes()
    {
        var mw = new DreamRecordTurnMiddleware(_taskRegistry);
        var ctx = new DreamContext
        {
            Request = new DreamRequest(),
            TaskId = "test-task",
            SessionIds = ["s1"],
            ConsolidationResult = "result",
        };

        await mw.InvokeAsync(ctx, (c, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        ctx.TurnRecorded.Should().BeTrue();
        ctx.TaskCompleted.Should().BeTrue();
        ctx.Result.Should().NotBeNull();
        ctx.Result!.IsSuccess.Should().BeTrue();
    }

    // === Full Pipeline ===

    [Fact]
    public async Task FullPipeline_AllStepsSucceed_ReturnsSuccess()
    {
        _sessionScanner.Setup(x => x.ListSessionsTouchedSinceAsync(It.IsAny<long>(), default))
            .ReturnsAsync(["s1", "s2"]);
        _chatClient.Setup(x => x.GetCompletionAsync(It.IsAny<MessageList>(), default))
            .ReturnsAsync("consolidated");

        var pipeline = new PipelineBuilder<DreamContext>()
            .WithShortCircuit(ctx => ctx.Result is not null)
            .Use(new DreamGateCheckMiddleware(_sessionScanner.Object, _config, NullLogger<DreamGateCheckMiddleware>.Instance))
            .Use(new DreamSessionScanMiddleware(_sessionScanner.Object, _config, NullLogger<DreamSessionScanMiddleware>.Instance))
            .Use(new DreamTaskRegisterMiddleware(_taskRegistry, _config))
            .Use(new DreamPromptBuildMiddleware())
            .Use(new DreamLlmConsolidateMiddleware(_chatClient.Object))
            .Use(new DreamRecordTurnMiddleware(_taskRegistry))
            .Build();

        var ctx = new DreamContext { Request = new DreamRequest() };
        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.Result.Should().NotBeNull();
        ctx.Result!.IsSuccess.Should().BeTrue();
        ctx.Result.SessionsProcessed.Should().Be(2);
    }

    [Fact]
    public async Task FullPipeline_GateFails_ReturnsSkipped()
    {
        var disabledConfig = new AutoDreamConfig { Enabled = false, MinHours = 1, MinSessions = 1 };

        var pipeline = new PipelineBuilder<DreamContext>()
            .WithShortCircuit(ctx => ctx.Result is not null)
            .Use(new DreamGateCheckMiddleware(_sessionScanner.Object, disabledConfig, NullLogger<DreamGateCheckMiddleware>.Instance))
            .Use(new DreamSessionScanMiddleware(_sessionScanner.Object, _config, NullLogger<DreamSessionScanMiddleware>.Instance))
            .Build();

        var ctx = new DreamContext { Request = new DreamRequest() };
        await pipeline.ExecuteAsync(ctx, CancellationToken.None).ConfigureAwait(true);

        ctx.Result.Should().NotBeNull();
        ctx.Result!.IsSkipped.Should().BeTrue();
        ctx.SessionsScanned.Should().BeFalse();
    }
}
