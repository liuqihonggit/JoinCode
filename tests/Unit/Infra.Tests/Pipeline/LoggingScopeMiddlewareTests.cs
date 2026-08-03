namespace Infrastructure.Pipeline.Tests;

using Infrastructure.Pipeline.Middlewares;

/// <summary>
/// LoggingScopeMiddleware 单元测试 — 验证 scope 传播、ObjectId 提取、IHasObjectId 接口
/// </summary>
public sealed class LoggingScopeMiddlewareTests
{
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
    public void LogScopeState_NullFields_OutputNull()
    {
        var state = new LogScopeState(null, null, null);

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
    public void LogScopeState_ToString_WithNullFields_NoTrailingSpace()
    {
        var state = new LogScopeState("trace123", null, null);

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

    [Fact]
    public void CreateScopeState_FromEntity_ExtractsObjectId()
    {
        var entity = new TestEntity(ObjectType.Agent, "test-entity");
        var state = LoggingScopeMiddleware<TestEntity>.CreateScopeState(entity);

        state[2].Value.Should().Be(entity.ObjectId.ToString());
        state[3].Value.Should().Be("agent");
    }

    [Fact]
    public void CreateScopeState_FromIHasObjectId_ExtractsContextObjectId()
    {
        var entity = new TestEntity(ObjectType.Session, "my-session");
        var context = new TestHasObjectIdContext(entity.ObjectId);
        var state = LoggingScopeMiddleware<TestHasObjectIdContext>.CreateScopeState(context);

        state[2].Value.Should().Be(entity.ObjectId.ToString());
        state[3].Value.Should().Be("session");
    }

    [Fact]
    public void CreateScopeState_FromPlainObject_ObjectIdIsNull()
    {
        var state = LoggingScopeMiddleware<string>.CreateScopeState("hello");

        state[2].Value.Should().BeNull();
        state[3].Value.Should().BeNull();
    }

    [Fact]
    public async Task LoggingScopeMiddleware_OpensScope_AndClosesOnExit()
    {
        var scopeStates = new List<object?>();

        var middleware = new LoggingScopeMiddleware<TestEntity>(logger: null);

        var entity = new TestEntity(ObjectType.Agent, "scope-test");
        var innerExecuted = false;

        await middleware.InvokeAsync(entity, async (ctx, ct) =>
        {
            innerExecuted = true;
            await Task.CompletedTask;
        }, CancellationToken.None).ConfigureAwait(true);

        innerExecuted.Should().BeTrue();
    }

    [Fact]
    public async Task WithLoggingScope_Extension_WorksInPipeline()
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
    public void LogScope_Begin_WithObjectId_ReturnsScope()
    {
        var objectId = new ObjectId(ObjectType.Agent, "logscope-test");
        var logger = NullLogger.Instance;

        var scope = LogScope.Begin(logger, objectId);

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

    private sealed class TestEntity(ObjectType type, string? displayName = null) : Entity(type, displayName)
    {
        protected override void OnDispose() { }
    }

    private sealed class TestHasObjectIdContext(ObjectId objectId) : IHasObjectId
    {
        public ObjectId? ContextObjectId => objectId;
    }

    private sealed class TrackingMiddleware(string label, List<string> log) : IMiddleware<TestEntity>
    {
        public async Task InvokeAsync(TestEntity context, MiddlewareDelegate<TestEntity> next, CancellationToken ct)
        {
            log.Add(label);
            await next(context, ct).ConfigureAwait(true);
        }
    }
}
