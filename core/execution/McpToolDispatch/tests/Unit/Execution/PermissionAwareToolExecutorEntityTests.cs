namespace McpToolRegistry.Tests;

public sealed class PermissionAwareToolExecutorEntityTests
{
    [Fact]
    public void ToolExecutionContext_HasExecutionEntity_AfterCreation()
    {
        var entity = new ToolExecutionEntity("read_file");
        var context = new ToolExecutionContext
        {
            ToolName = "read_file",
            Arguments = [],
            ExecutionEntity = entity,
        };

        context.ExecutionEntity.Should().BeSameAs(entity);
        context.ExecutionEntity!.ToolName.Should().Be("read_file");
    }

    [Fact]
    public void ToolExecutionContext_ExecutionEntity_IsNullByDefault()
    {
        var context = new ToolExecutionContext
        {
            ToolName = "test",
            Arguments = [],
        };

        context.ExecutionEntity.Should().BeNull();
    }

    [Fact]
    public void ToolExecutionEntity_LifecycleFlow_MirrorsExecutorFlow()
    {
        var entity = new ToolExecutionEntity("bash");

        entity.LifecycleState.Should().Be(EntityLifecycle.Created);

        entity.LifecycleState = EntityLifecycle.Active;
        entity.StartedAt = DateTime.UtcNow;
        entity.LifecycleState.Should().Be(EntityLifecycle.Active);

        entity.LifecycleState = EntityLifecycle.Completed;
        entity.CompletedAt = DateTime.UtcNow;
        entity.IsError = false;
        entity.ResultSummary = "command executed";
        entity.LifecycleState.Should().Be(EntityLifecycle.Completed);
        entity.IsError.Should().BeFalse();
        entity.ResultSummary.Should().Be("command executed");

        entity.Dispose();
    }

    [Fact]
    public void ToolExecutionEntity_ErrorFlow_SetsIsError()
    {
        var entity = new ToolExecutionEntity("grep");

        entity.LifecycleState = EntityLifecycle.Active;
        entity.StartedAt = DateTime.UtcNow;

        entity.LifecycleState = EntityLifecycle.Completed;
        entity.CompletedAt = DateTime.UtcNow;
        entity.IsError = true;
        entity.ResultSummary = "pattern not found";

        entity.IsError.Should().BeTrue();
        entity.ResultSummary.Should().Be("pattern not found");

        entity.Dispose();
    }

    [Fact]
    public void ToolExecutionEntity_RegisteredInGlobalRegistry()
    {
        var entity = new ToolExecutionEntity("web_fetch");
        try
        {
            ToolExecutionEntity.Registry.Get(entity.ObjectId).Should().BeSameAs(entity);
            ToolExecutionEntity.Registry.GetByToolName("web_fetch").Should().Contain(entity);
        }
        finally { entity.Dispose(); }
    }

    [Fact]
    public void ToolExecutionEntity_SpanId_LinkedToTelemetry()
    {
        var entity = new ToolExecutionEntity("bash", spanId: "span_abc123");
        try
        {
            entity.SpanId.Should().Be("span_abc123");
        }
        finally { entity.Dispose(); }
    }
}
