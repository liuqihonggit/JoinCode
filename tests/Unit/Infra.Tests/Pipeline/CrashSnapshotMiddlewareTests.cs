namespace Infrastructure.Pipeline.Tests;

using Infrastructure.Pipeline.Middlewares;

public sealed class CrashSnapshotMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_NoException_DoesNotRecordSnapshot()
    {
        var store = new CrashSnapshotStore();
        var middleware = new CrashSnapshotMiddleware<TestCtx>(store, "TestPipe");
        var pipeline = new MiddlewarePipeline<TestCtx>([middleware, new OkMiddleware()]);

        await pipeline.ExecuteAsync(new TestCtx(), CancellationToken.None);

        store.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task InvokeAsync_Exception_RecordsSnapshotAndRethrows()
    {
        var store = new CrashSnapshotStore();
        var middleware = new CrashSnapshotMiddleware<TestCtx>(store, "CrashPipe");
        var pipeline = new MiddlewarePipeline<TestCtx>([middleware, new ThrowMiddleware()]);

        var act = async () => await pipeline.ExecuteAsync(new TestCtx(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("boom");
        store.TotalCount.Should().Be(1);
        var snapshot = store.GetRecent(1)[0];
        snapshot.FenceName.Should().Be("CrashPipe");
        snapshot.Severity.Should().Be(CrashSeverity.Error);
        snapshot.ExceptionType.Should().Contain("InvalidOperationException");
    }

    [Fact]
    public async Task InvokeAsync_OperationCanceledException_DoesNotRecordSnapshot()
    {
        var store = new CrashSnapshotStore();
        var middleware = new CrashSnapshotMiddleware<TestCtx>(store, "CancelPipe");
        var pipeline = new MiddlewarePipeline<TestCtx>([middleware, new CancelMiddleware()]);

        var act = async () => await pipeline.ExecuteAsync(new TestCtx(), CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
        store.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task InvokeAsync_ContextExtractor_PopulatesExecutionContext()
    {
        var store = new CrashSnapshotStore();
        var middleware = new CrashSnapshotMiddleware<TestCtx>(store, "CtxPipe",
            ctx => new CrashExecutionContext { ToolName = "MyTool", TurnIndex = ctx.Turn });
        var pipeline = new MiddlewarePipeline<TestCtx>([middleware, new ThrowMiddleware()]);

        var testCtx = new TestCtx { Turn = 7 };
        var act = async () => await pipeline.ExecuteAsync(testCtx, CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        var snapshot = store.GetRecent(1)[0];
        snapshot.ExecutionContext.ToolName.Should().Be("MyTool");
        snapshot.ExecutionContext.TurnIndex.Should().Be(7);
    }

    [Fact]
    public async Task InvokeAsync_NoContextExtractor_DefaultsToPipelineName()
    {
        var store = new CrashSnapshotStore();
        var middleware = new CrashSnapshotMiddleware<TestCtx>(store, "DefaultCtxPipe");
        var pipeline = new MiddlewarePipeline<TestCtx>([middleware, new ThrowMiddleware()]);

        var act = async () => await pipeline.ExecuteAsync(new TestCtx(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>();
        var snapshot = store.GetRecent(1)[0];
        snapshot.ExecutionContext.OperationName.Should().Be("DefaultCtxPipe");
    }

    [Fact]
    public async Task InvokeAsync_WorkflowException_ExtractsErrorCode()
    {
        var store = new CrashSnapshotStore();
        var middleware = new CrashSnapshotMiddleware<TestCtx>(store, "WfPipe");
        var pipeline = new MiddlewarePipeline<TestCtx>([middleware, new WfThrowMiddleware()]);

        var act = async () => await pipeline.ExecuteAsync(new TestCtx(), CancellationToken.None);

        await act.Should().ThrowAsync<WorkflowException>();
        var snapshot = store.GetRecent(1)[0];
        snapshot.ErrorCode.Should().Be("WF003");
        snapshot.ErrorCategory.Should().Be(ErrorCategory.Workflow);
    }

    [Fact]
    public async Task InvokeAsync_ContinueError_Swallowed_NoSnapshotRecorded()
    {
        var store = new CrashSnapshotStore();
        var crashMw = new CrashSnapshotMiddleware<TestCtx>(store, "ContinuePipe");
        var throwContinue = new ContinueThrowMiddleware();
        var ok = new OkMiddleware();
        var pipeline = new MiddlewarePipeline<TestCtx>(
            [crashMw, throwContinue, ok],
            onError: (_, _) => { });

        await pipeline.ExecuteAsync(new TestCtx(), CancellationToken.None);

        store.TotalCount.Should().Be(0);
    }

    [Fact]
    public void OnError_Returns_Propagate()
    {
        var store = new CrashSnapshotStore();
        var middleware = new CrashSnapshotMiddleware<TestCtx>(store, "PropPipe");

        middleware.OnError.Should().Be(ErrorBehavior.Propagate);
    }

    [Fact]
    public void Constructor_NullStore_Throws()
    {
        var act = () => new CrashSnapshotMiddleware<TestCtx>(null!, "Pipe");

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_EmptyPipelineName_Throws()
    {
        var act = () => new CrashSnapshotMiddleware<TestCtx>(new CrashSnapshotStore(), "");

        act.Should().Throw<ArgumentException>();
    }

    private sealed class TestCtx
    {
        public int Turn { get; set; }
    }

    private sealed class OkMiddleware : IMiddleware<TestCtx>
    {
        public ErrorBehavior OnError => ErrorBehavior.Continue;
        public Task InvokeAsync(TestCtx context, MiddlewareDelegate<TestCtx> next, CancellationToken ct)
            => next(context, ct);
    }

    private sealed class ThrowMiddleware : IMiddleware<TestCtx>
    {
        public ErrorBehavior OnError => ErrorBehavior.Propagate;
        public Task InvokeAsync(TestCtx context, MiddlewareDelegate<TestCtx> next, CancellationToken ct)
            => throw new InvalidOperationException("boom");
    }

    private sealed class CancelMiddleware : IMiddleware<TestCtx>
    {
        public ErrorBehavior OnError => ErrorBehavior.Propagate;
        public Task InvokeAsync(TestCtx context, MiddlewareDelegate<TestCtx> next, CancellationToken ct)
            => throw new OperationCanceledException(ct);
    }

    private sealed class WfThrowMiddleware : IMiddleware<TestCtx>
    {
        public ErrorBehavior OnError => ErrorBehavior.Propagate;
        public Task InvokeAsync(TestCtx context, MiddlewareDelegate<TestCtx> next, CancellationToken ct)
            => throw new WorkflowException("wf error", ErrorCode.WorkflowExecution.ToValue());
    }

    private sealed class ContinueThrowMiddleware : IMiddleware<TestCtx>
    {
        public ErrorBehavior OnError => ErrorBehavior.Continue;
        public Task InvokeAsync(TestCtx context, MiddlewareDelegate<TestCtx> next, CancellationToken ct)
            => throw new InvalidOperationException("continue boom");
    }
}
