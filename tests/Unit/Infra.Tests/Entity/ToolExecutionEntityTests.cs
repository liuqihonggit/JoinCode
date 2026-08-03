namespace Infra.Tests.EntityTests;

public sealed class ToolExecutionEntityTests
{
    [Fact]
    public void Constructor_SetsToolNameAndRegistersToRegistry()
    {
        var entity = new ToolExecutionEntity("read_file");
        try
        {
            entity.ToolName.Should().Be("read_file");
            entity.ObjectId.Type.Should().Be(ObjectType.Tool);
            entity.LifecycleState.Should().Be(EntityLifecycle.Created);
            ToolExecutionEntity.Registry.Get(entity.ObjectId).Should().BeSameAs(entity);
        }
        finally { entity.Dispose(); }
    }

    [Fact]
    public void Constructor_WithOptionalFields_SetsAllProperties()
    {
        var entity = new ToolExecutionEntity("bash", toolUseId: "tu_123", spanId: "span_456", displayName: "my bash");
        try
        {
            entity.ToolName.Should().Be("bash");
            entity.ToolUseId.Should().Be("tu_123");
            entity.SpanId.Should().Be("span_456");
            entity.DisplayName.Should().Be("my bash");
        }
        finally { entity.Dispose(); }
    }

    [Fact]
    public void Constructor_DefaultDisplayName_IsToolName()
    {
        var entity = new ToolExecutionEntity("grep");
        try { entity.DisplayName.Should().Be("grep"); }
        finally { entity.Dispose(); }
    }

    [Fact]
    public void Dispose_RemovesFromRegistry()
    {
        var entity = new ToolExecutionEntity("test");
        var objectId = entity.ObjectId;
        ToolExecutionEntity.Registry.Get(objectId).Should().BeSameAs(entity);
        entity.Dispose();
        ToolExecutionEntity.Registry.Get(objectId).Should().BeNull();
    }

    [Fact]
    public void Dispose_SetsLifecycleToDisposed()
    {
        var entity = new ToolExecutionEntity("test");
        entity.LifecycleState = EntityLifecycle.Active;
        entity.Dispose();
        entity.LifecycleState.Should().Be(EntityLifecycle.Disposed);
    }

    [Fact]
    public void LifecycleTransition_CreatedToActiveToCompleted()
    {
        var entity = new ToolExecutionEntity("test");
        try
        {
            entity.LifecycleState.Should().Be(EntityLifecycle.Created);
            entity.LifecycleState = EntityLifecycle.Active;
            entity.StartedAt = DateTime.UtcNow;
            entity.LifecycleState.Should().Be(EntityLifecycle.Active);
            entity.LifecycleState = EntityLifecycle.Completed;
            entity.CompletedAt = DateTime.UtcNow;
            entity.LifecycleState.Should().Be(EntityLifecycle.Completed);
        }
        finally { entity.Dispose(); }
    }

    [Fact]
    public void ResultSummary_CanBeSetAfterCompletion()
    {
        var entity = new ToolExecutionEntity("test");
        try
        {
            entity.ResultSummary.Should().BeNull();
            entity.ResultSummary = "file content read successfully";
            entity.ResultSummary.Should().Be("file content read successfully");
        }
        finally { entity.Dispose(); }
    }

    [Fact]
    public void IsError_CanBeSet()
    {
        var entity = new ToolExecutionEntity("test");
        try
        {
            entity.IsError.Should().BeFalse();
            entity.IsError = true;
            entity.IsError.Should().BeTrue();
        }
        finally { entity.Dispose(); }
    }

    [Fact]
    public void TraceId_CapturedFromActivityCurrent()
    {
        var activity = new System.Diagnostics.Activity("test-activity");
        activity.Start();
        var entity = new ToolExecutionEntity("test");
        try { entity.TraceId.Should().NotBeNull(); }
        finally { entity.Dispose(); activity.Dispose(); }
    }

    [Fact]
    public void TraceId_NullWhenNoActivity()
    {
        var entity = new ToolExecutionEntity("test");
        try { entity.TraceId.Should().BeNull(); }
        finally { entity.Dispose(); }
    }

    [Fact]
    public void Subclass_BashProcessEntity_RegistersToBaseRegistry()
    {
        var entity = new BashProcessEntity(command: "dotnet build");
        try
        {
            entity.ToolName.Should().Be("bash");
            entity.Command.Should().Be("dotnet build");
            entity.ObjectId.Type.Should().Be(ObjectType.Bash);
            ToolExecutionEntity.Registry.Get(entity.ObjectId).Should().BeSameAs(entity);
        }
        finally { entity.Dispose(); }
    }

    [Fact]
    public void Subclass_BashProcessEntity_CanReclaim_RequiresExitCode()
    {
        var entity = new BashProcessEntity();
        try
        {
            entity.LifecycleState = EntityLifecycle.Completed;
            entity.CompletedAt = DateTime.UtcNow;
            entity.MarkPersisted();
            entity.CanReclaim().Should().BeFalse();
            entity.ExitCode = 0;
            entity.CanReclaim().Should().BeTrue();
        }
        finally { entity.Dispose(); }
    }
}
