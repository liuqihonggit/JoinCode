namespace Infra.Tests.EntityTests;

public sealed class ToolExecutionEntityRegistryTests {
    [Fact]
    public async Task Add_And_Get_ByObjectId() {
        await using var entity = new ToolExecutionEntity("test");
        ToolExecutionEntity.Registry.Get(entity.ObjectId).Should().BeSameAs(entity);
    }

    [Fact]
    public void Get_UnknownId_ReturnsNull() {
        ToolExecutionEntity.Registry.Get(new ObjectId(ObjectType.Tool, "nonexistent")).Should().BeNull();
    }

    [Fact]
    public async Task Remove_OnDispose() {
        await using var entity = new ToolExecutionEntity("test");
        var objectId = entity.ObjectId;
        ToolExecutionEntity.Registry.Get(objectId).Should().BeSameAs(entity);
        await entity.DisposeAsync();
        ToolExecutionEntity.Registry.Get(objectId).Should().BeNull();
    }

    [Fact]
    public async Task GetAll_ContainsCreatedEntity() {
        await using var entity = new ToolExecutionEntity("bash");
        ToolExecutionEntity.Registry.GetAll().Should().Contain(entity);
    }

    [Fact]
    public async Task GetActive_ReturnsActiveEntity() {
        await using var entity = new ToolExecutionEntity("bash");
        entity.LifecycleState = EntityLifecycle.Active;
        ToolExecutionEntity.Registry.GetActive().Should().Contain(entity);
    }

    [Fact]
    public async Task GetActive_ExcludesCompletedEntity() {
        await using var entity = new ToolExecutionEntity("bash");
        ToolExecutionEntity.Registry.GetActive().Should().NotContain(entity);
    }

    [Fact]
    public async Task GetCompleted_ReturnsCompletedEntity() {
        await using var entity = new ToolExecutionEntity("bash");
        entity.LifecycleState = EntityLifecycle.Completed;
        ToolExecutionEntity.Registry.GetCompleted().Should().Contain(entity);
    }

    [Fact]
    public async Task GetTimedOut_ReturnsTimedOutEntity() {
        await using var entity = new ToolExecutionEntity("bash") { TimeoutAt = DateTime.UtcNow.AddSeconds(-1) };
        ToolExecutionEntity.Registry.GetTimedOut().Should().Contain(entity);
    }

    [Fact]
    public async Task GetTimedOut_ExcludesNonTimedOutEntity() {
        await using var entity = new ToolExecutionEntity("read_file");
        ToolExecutionEntity.Registry.GetTimedOut().Should().NotContain(entity);
    }

    [Fact]
    public async Task GetByToolName_FiltersCaseInsensitive() {
        await using var e1 = new ToolExecutionEntity("bash");
        await using var e2 = new ToolExecutionEntity("Bash");
        var result = ToolExecutionEntity.Registry.GetByToolName("bash");
        result.Should().Contain(e1);
        result.Should().Contain(e2);
    }

    [Fact]
    public async Task GetByToolName_ExcludesOtherToolNames() {
        await using var bash = new ToolExecutionEntity("bash");
        await using var grep = new ToolExecutionEntity("grep");
        var result2 = ToolExecutionEntity.Registry.GetByToolName("bash");
        result2.Should().Contain(bash);
        result2.Should().NotContain(grep);
    }

    [Fact]
    public async Task SubclassEntities_QueryableFromBaseRegistry() {
        await using var bash = new BashProcessEntity(command: "ls");
        await using var web = new WebFetchEntity(url: "https://example.com");
        await using var sleep = new SleepEntity(durationSeconds: 10);
        await using var repl = new ReplSessionEntity(language: "csharp");
        await using var ask = new UserInteractionEntity(question: "Continue?");
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
}