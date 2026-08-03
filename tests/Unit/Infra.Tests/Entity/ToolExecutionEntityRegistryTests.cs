namespace Infra.Tests.EntityTests;

public sealed class ToolExecutionEntityRegistryTests
{
    [Fact]
    public void Add_And_Get_ByObjectId()
    {
        var entity = new ToolExecutionEntity("test");
        try { ToolExecutionEntity.Registry.Get(entity.ObjectId).Should().BeSameAs(entity); }
        finally { entity.Dispose(); }
    }

    [Fact]
    public void Get_UnknownId_ReturnsNull()
    {
        ToolExecutionEntity.Registry.Get(new ObjectId(ObjectType.Tool, "nonexistent")).Should().BeNull();
    }

    [Fact]
    public void Remove_OnDispose()
    {
        var entity = new ToolExecutionEntity("test");
        var objectId = entity.ObjectId;
        ToolExecutionEntity.Registry.Get(objectId).Should().BeSameAs(entity);
        entity.Dispose();
        ToolExecutionEntity.Registry.Get(objectId).Should().BeNull();
    }

    [Fact]
    public void GetAll_ContainsCreatedEntity()
    {
        var entity = new ToolExecutionEntity("bash");
        try { ToolExecutionEntity.Registry.GetAll().Should().Contain(entity); }
        finally { entity.Dispose(); }
    }

    [Fact]
    public void GetActive_ReturnsActiveEntity()
    {
        var entity = new ToolExecutionEntity("bash");
        entity.LifecycleState = EntityLifecycle.Active;
        try { ToolExecutionEntity.Registry.GetActive().Should().Contain(entity); }
        finally { entity.Dispose(); }
    }

    [Fact]
    public void GetActive_ExcludesCompletedEntity()
    {
        var entity = new ToolExecutionEntity("bash");
        entity.LifecycleState = EntityLifecycle.Completed;
        try { ToolExecutionEntity.Registry.GetActive().Should().NotContain(entity); }
        finally { entity.Dispose(); }
    }

    [Fact]
    public void GetCompleted_ReturnsCompletedEntity()
    {
        var entity = new ToolExecutionEntity("bash");
        entity.LifecycleState = EntityLifecycle.Completed;
        try { ToolExecutionEntity.Registry.GetCompleted().Should().Contain(entity); }
        finally { entity.Dispose(); }
    }

    [Fact]
    public void GetTimedOut_ReturnsTimedOutEntity()
    {
        var entity = new ToolExecutionEntity("bash") { TimeoutAt = DateTime.UtcNow.AddSeconds(-1) };
        try { ToolExecutionEntity.Registry.GetTimedOut().Should().Contain(entity); }
        finally { entity.Dispose(); }
    }

    [Fact]
    public void GetTimedOut_ExcludesNonTimedOutEntity()
    {
        var entity = new ToolExecutionEntity("read_file");
        try { ToolExecutionEntity.Registry.GetTimedOut().Should().NotContain(entity); }
        finally { entity.Dispose(); }
    }

    [Fact]
    public void GetByToolName_FiltersCaseInsensitive()
    {
        var e1 = new ToolExecutionEntity("bash");
        var e2 = new ToolExecutionEntity("Bash");
        try
        {
            var result = ToolExecutionEntity.Registry.GetByToolName("bash");
            result.Should().Contain(e1);
            result.Should().Contain(e2);
        }
        finally { e1.Dispose(); e2.Dispose(); }
    }

    [Fact]
    public void GetByToolName_ExcludesOtherToolNames()
    {
        var bash = new ToolExecutionEntity("bash");
        var grep = new ToolExecutionEntity("grep");
        try
        {
            var result = ToolExecutionEntity.Registry.GetByToolName("bash");
            result.Should().Contain(bash);
            result.Should().NotContain(grep);
        }
        finally { bash.Dispose(); grep.Dispose(); }
    }

    [Fact]
    public void SubclassEntities_QueryableFromBaseRegistry()
    {
        var bash = new BashProcessEntity(command: "ls");
        var web = new WebFetchEntity(url: "https://example.com");
        var sleep = new SleepEntity(durationSeconds: 10);
        var repl = new ReplSessionEntity(language: "csharp");
        var ask = new UserInteractionEntity(question: "Continue?");
        try
        {
            ToolExecutionEntity.Registry.Get(bash.ObjectId).Should().BeSameAs(bash);
            ToolExecutionEntity.Registry.Get(web.ObjectId).Should().BeSameAs(web);
            ToolExecutionEntity.Registry.Get(sleep.ObjectId).Should().BeSameAs(sleep);
            ToolExecutionEntity.Registry.Get(repl.ObjectId).Should().BeSameAs(repl);
            ToolExecutionEntity.Registry.Get(ask.ObjectId).Should().BeSameAs(ask);

            ToolExecutionEntity.Registry.GetByToolName("bash").Should().Contain(bash);
            ToolExecutionEntity.Registry.GetByToolName("web_fetch").Should().Contain(web);
            ToolExecutionEntity.Registry.GetByToolName("sleep").Should().Contain(sleep);
            ToolExecutionEntity.Registry.GetByToolName("repl").Should().Contain(repl);
            ToolExecutionEntity.Registry.GetByToolName("ask_user").Should().Contain(ask);
        }
        finally
        {
            bash.Dispose(); web.Dispose(); sleep.Dispose(); repl.Dispose(); ask.Dispose();
        }
    }
}
