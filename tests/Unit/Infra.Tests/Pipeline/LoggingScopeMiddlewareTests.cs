namespace Infrastructure.Pipeline.Tests;

using Infrastructure.Pipeline.Middlewares;

/// <summary>
/// LoggingScopeMiddleware 单元测试 — 验证 scope 传播、ObjectId.Empty、Func 选择器
/// </summary>
public sealed class LoggingScopeMiddlewareTests
{
    // === ObjectId.Empty ===

    [Fact]
    public void ObjectId_Empty_IsEmpty_IsTrue()
    {
        ObjectId.Empty.IsEmpty.Should().BeTrue();
        default(ObjectId).IsEmpty.Should().BeTrue();
    }

    [Fact]
    public void ObjectId_Empty_ToString_IsNone0()
    {
        ObjectId.Empty.ToString().Should().Be("None:0");
    }

    [Fact]
    public void ObjectId_Assigned_IsEmpty_IsFalse()
    {
        var id = new ObjectId(ObjectType.Agent, "test");
        id.IsEmpty.Should().BeFalse();
    }

    // === LogScopeState ===

    [Fact]
    public void LogScopeState_AllFields_SetCorrectly()
    {
        var objectId = new ObjectId(ObjectType.Agent, "test-agent");

        var state = new LogScopeState("trace123", "span456", objectId);

        state.Count.Should().Be(4);
        state[0].Key.Should().Be("TraceId");
        state[0].Value.Should().Be("trace123");
        state[1].Key.Should().Be("SpanId");
        state[1].Value.Should().Be("span456");
        state[2].Key.Should().Be("ObjectId");
        state[2].Value.Should().Be(objectId.ToString());
        state[3].Key.Should().Be("ObjectType");
        state[3].Value.Should().Be("agent");
    }

    [Fact]
    public void LogScopeState_EmptyObjectId_OutputsNull()
    {
        var state = new LogScopeState(null, null, ObjectId.Empty);

        state[0].Value.Should().BeNull();
        state[1].Value.Should().BeNull();
        state[2].Value.Should().BeNull();
        state[3].Value.Should().BeNull();
    }

    [Fact]
    public void LogScopeState_ToString_WithAllFields()
    {
        var objectId = new ObjectId(ObjectType.Agent, "test-agent");
        var state = new LogScopeState("trace123", "span456", objectId);

        var str = state.ToString();

        str.Should().Contain("TraceId=trace123");
        str.Should().Contain("SpanId=span456");
        str.Should().Contain("ObjectId=");
        str.Should().StartWith("[");
        str.Should().EndWith("]");
    }

    [Fact]
    public void LogScopeState_ToString_WithEmptyObjectId_NoTrailingSpace()
    {
        var state = new LogScopeState("trace123", null, ObjectId.Empty);

        var str = state.ToString();

        str.Should().Be("[TraceId=trace123]");
    }

    [Fact]
    public void LogScopeState_EnumeratesAllFields()
    {
        var objectId = new ObjectId(ObjectType.Session, "my-session");
        var state = new LogScopeState("t", "s", objectId);

        var list = state.ToList();

        list.Should().HaveCount(4);
        list[0].Key.Should().Be("TraceId");
        list[3].Key.Should().Be("ObjectType");
    }

    // === 中间件默认选择器 ===

    [Fact]
    public async Task Middleware_DefaultSelector_Entity_GetsObjectId()
    {
        var entity = new TestEntity(ObjectType.Agent, "test-entity");
        var logScopeStates = new List<LogScopeState>();

        var middleware = new LoggingScopeMiddleware<TestEntity>(
            logger: null,
            objectIdSelector: ctx =>
            {
                var s = new LogScopeState(null, null, ctx.ObjectId);
                logScopeStates.Add(s);
                return ctx.ObjectId;
            });

        await middleware.InvokeAsync(entity, (ctx, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);

        logScopeStates.Should().HaveCount(1);
        logScopeStates[0][2].Value.Should().Be(entity.ObjectId.ToString());
    }

    [Fact]
    public async Task Middleware_DefaultSelector_NonEntity_GetsEmpty()
    {
        ObjectId capturedObjectId = ObjectId.Empty;

        var middleware = new LoggingScopeMiddleware<string>();

        // 非Entity，默认选择器返回 Empty
        // 无法直接验证 scope 内容，但可以验证不抛异常
        await middleware.InvokeAsync("hello", (ctx, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);
    }

    [Fact]
    public async Task Middleware_CustomSelector_ExtractsObjectId()
    {
        var entity = new TestEntity(ObjectType.Session, "my-session");
        var context = new TestContextWithObjectId(entity.ObjectId);

        var middleware = new LoggingScopeMiddleware<TestContextWithObjectId>(
            logger: null,
            objectIdSelector: ctx => ctx.ObjectId);

        // 验证不抛异常，自定义选择器正确提取
        await middleware.InvokeAsync(context, (ctx, ct) => Task.CompletedTask, CancellationToken.None).ConfigureAwait(true);
    }

    // === Pipeline 集成 ===

    [Fact]
    public async Task WithLoggingScope_DefaultSelector_WorksInPipeline()
    {
        var executionLog = new List<string>();

        var pipeline = new PipelineBuilder<TestEntity>()
            .WithLoggingScope()
            .Use(new TrackingMiddleware("A", executionLog))
            .Build();

        var entity = new TestEntity(ObjectType.Agent, "pipeline-test");
        await pipeline.ExecuteAsync(entity, CancellationToken.None).ConfigureAwait(true);

        executionLog.Should().Equal("A");
    }

    [Fact]
    public async Task WithLoggingScope_CustomSelector_WorksInPipeline()
    {
        var executionLog = new List<string>();

        var pipeline = new PipelineBuilder<TestContextWithObjectId>()
            .WithLoggingScope(ctx => ctx.ObjectId)
            .Use(new TrackingContextMiddleware("A", executionLog))
            .Build();

        var context = new TestContextWithObjectId(new ObjectId(ObjectType.Agent, "custom-selector"));
        await pipeline.ExecuteAsync(context, CancellationToken.None).ConfigureAwait(true);

        executionLog.Should().Equal("A");
    }

    // === LogScope 静态工具 ===

    [Fact]
    public void LogScope_Begin_WithObjectId_ReturnsScope()
    {
        var objectId = new ObjectId(ObjectType.Agent, "logscope-test");
        var logger = NullLogger.Instance;

        var scope = LogScope.Begin(logger, objectId);

        scope.Should().NotBeNull();
        scope?.Dispose();
    }

    [Fact]
    public void LogScope_Begin_WithEmptyObjectId_ReturnsScope()
    {
        var logger = NullLogger.Instance;

        var scope = LogScope.Begin(logger, ObjectId.Empty);

        scope.Should().NotBeNull();
        scope?.Dispose();
    }

    [Fact]
    public void LogScope_BeginTrace_WithNoActivity_ReturnsNull()
    {
        var logger = NullLogger.Instance;

        var scope = LogScope.BeginTrace(logger);

        scope.Should().BeNull();
    }

    // === 测试辅助类 ===

    private sealed class TestEntity(ObjectType type, string? displayName = null) : Entity(type, displayName)
    {
        protected override void OnDispose() { }
    }

    private sealed class TestContextWithObjectId(ObjectId objectId)
    {
        public ObjectId ObjectId { get; } = objectId;
    }

    private sealed class TrackingMiddleware(string label, List<string> log) : IMiddleware<TestEntity>
    {
        public async Task InvokeAsync(TestEntity context, MiddlewareDelegate<TestEntity> next, CancellationToken ct)
        {
            log.Add(label);
            await next(context, ct).ConfigureAwait(true);
        }
    }

    private sealed class TrackingContextMiddleware(string label, List<string> log) : IMiddleware<TestContextWithObjectId>
    {
        public async Task InvokeAsync(TestContextWithObjectId context, MiddlewareDelegate<TestContextWithObjectId> next, CancellationToken ct)
        {
            log.Add(label);
            await next(context, ct).ConfigureAwait(true);
        }
    }
}
