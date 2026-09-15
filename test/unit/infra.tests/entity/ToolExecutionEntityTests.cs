namespace Infra.Tests.EntityTests;

public sealed class ToolExecutionEntityTests
{
    [Fact]
    public void Constructor_SetsToolNameAndRegistersToRegistry()
    {
        using var entity =  new ToolExecutionEntity("read_file");
        entity.ToolName.Should().Be("read_file");
        entity.ObjectId.Type.Should().Be(ObjectType.Tool);
        entity.LifecycleState.Should().Be(EntityLifecycle.Created);
        ToolExecutionEntity.Registry.Get(entity.ObjectId).Should().BeSameAs(entity);
    
    }

    [Fact]
    public void Constructor_WithOptionalFields_SetsAllProperties()
    {
        using var entity =  new ToolExecutionEntity("bash", toolUseId: "tu_123", spanId: "span_456", displayName: "my bash");
        entity.ToolName.Should().Be("bash");
        entity.ToolUseId.Should().Be("tu_123");
        entity.SpanId.Should().Be("span_456");
        entity.DisplayName.Should().Be("my bash");
    
    }

    [Fact]
    public void Constructor_DefaultDisplayName_IsToolName()
    {
        using var entity =  new ToolExecutionEntity("grep");
 entity.DisplayName.Should().Be("grep"); 
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
        using var entity =  new ToolExecutionEntity("test");
        entity.LifecycleState.Should().Be(EntityLifecycle.Created);
        entity.LifecycleState = EntityLifecycle.Active;
        entity.StartedAt = DateTime.UtcNow;
        entity.LifecycleState.Should().Be(EntityLifecycle.Active);
        entity.LifecycleState = EntityLifecycle.Completed;
        entity.CompletedAt = DateTime.UtcNow;
        entity.LifecycleState.Should().Be(EntityLifecycle.Completed);
    
    }

    [Fact]
    public void ResultSummary_CanBeSetAfterCompletion()
    {
        using var entity =  new ToolExecutionEntity("test");
        entity.ResultSummary.Should().BeNull();
        entity.ResultSummary = "file content read successfully";
        entity.ResultSummary.Should().Be("file content read successfully");
    
    }

    [Fact]
    public void IsError_CanBeSet()
    {
        using var entity =  new ToolExecutionEntity("test");
        entity.IsError.Should().BeFalse();
        entity.IsError = true;
        entity.IsError.Should().BeTrue();
    
    }

    [Fact]
    public void TraceId_CapturedFromActivityCurrent()
    {
        using var activity = new System.Diagnostics.Activity("test-activity");
        activity.Start();
        using var entity = new ToolExecutionEntity("test");
        entity.TraceId.Should().NotBeNull();
    }

    [Fact]
    public void TraceId_NullWhenNoActivity()
    {
        using var entity =  new ToolExecutionEntity("test");
 entity.TraceId.Should().BeNull(); 
    }

    [Fact]
    public void Subclass_BashProcessEntity_RegistersToBaseRegistry()
    {
        using var entity =  new BashProcessEntity(command: "dotnet build");
        entity.ToolName.Should().Be("bash");
        entity.Command.Should().Be("dotnet build");
        entity.ObjectId.Type.Should().Be(ObjectType.ShellCommand);
        ToolExecutionEntity.Registry.Get(entity.ObjectId).Should().BeSameAs(entity);
    
    }

    [Fact]
    public void Subclass_BashProcessEntity_CanReclaim_RequiresExitCode()
    {
        using var entity =  new BashProcessEntity();
        entity.LifecycleState = EntityLifecycle.Completed;
        entity.CompletedAt = DateTime.UtcNow;
        entity.MarkPersisted();
        entity.CanReclaim().Should().BeFalse();
        entity.ExitCode = 0;
        entity.CanReclaim().Should().BeTrue();
    
    }
}
