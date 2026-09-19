namespace Core.Tests.Plugins;

public sealed class WorkflowPluginBaseTests {
    private sealed class TestPlugin : WorkflowPluginBase {
        public override string Name => "test-plugin";
        public override string Version => "1.0.0";
        public override string Description => "Test plugin";
        public OperationResult LoadResult { get; set; } = OperationResult.Ok();
        public int OnUnloadCallCount { get; private set; }

        public TestPlugin() : base("test-plugin") { }

        public override Task<OperationResult> LoadAsync(PluginContext ctx, CancellationToken cancellationToken = default)
            => Task.FromResult(LoadResult);
        public override Task<OperationResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
            => Task.FromResult(LoadResult);

        public new T RegisterResource<T>(T resource) where T : PluginResourceBase => base.RegisterResource(resource);

        protected override void OnUnload() => OnUnloadCallCount++;
    }

    private sealed class TestResource : PluginResourceBase {
        public TestResource(string owner, string name) : base(owner, PluginResourceKind.Command, name) { }
    }

    [Fact]
    public void WorkflowPluginBase_HasObjectIdWithTypePlugin() {
        using var plugin = new TestPlugin();
        plugin.ObjectId.Type.Should().Be(ObjectType.Plugin);
    }

    [Fact]
    public void RegisterResource_AddsToResourcesCollection() {
        using var plugin = new TestPlugin();
        var resource = plugin.RegisterResource(new TestResource("test-plugin", "cmd1"));

        plugin.Resources.Should().Contain(resource);
        plugin.Resources.Should().HaveCount(1);
    }

    [Fact]
    public void Unload_ReleasesAllResources() {
        var plugin = new TestPlugin();
        var r1 = plugin.RegisterResource(new TestResource("test-plugin", "cmd1"));
        var r2 = plugin.RegisterResource(new TestResource("test-plugin", "cmd2"));
        var r1Id = r1.ObjectId;
        var r2Id = r2.ObjectId;

        ObjectIdManager.IsRegistered(r1Id).Should().BeTrue();
        ObjectIdManager.IsRegistered(r2Id).Should().BeTrue();

        var result = plugin.Unload();

        result.IsSuccess.Should().BeTrue();
        plugin.Resources.Should().BeEmpty();
        ObjectIdManager.IsRegistered(r1Id).Should().BeFalse();
        ObjectIdManager.IsRegistered(r2Id).Should().BeFalse();
    }

    [Fact]
    public void Unload_MarksDead() {
        var plugin = new TestPlugin();

        plugin.Unload();

        plugin.IsAlive.Should().BeFalse();
    }

    [Fact]
    public void Unload_CallsOnUnload() {
        var plugin = new TestPlugin();

        plugin.Unload();

        plugin.OnUnloadCallCount.Should().Be(1);
    }

    [Fact]
    public void Unload_ReleasesUnmanagedResources() {
        var plugin = new TestPlugin();
        var handle = new TestSafeHandle();
        plugin.UnmanagedResources.Register("buf1", handle, 1024);

        plugin.Unload();

        handle.IsClosed.Should().BeTrue();
        plugin.UnmanagedResources.Count.Should().Be(0);
    }

    [Fact]
    public void Touch_UpdatesHeartbeat() {
        using var plugin = new TestPlugin();

        plugin.Touch();
        plugin.LastHeartbeatAt.Should().BeOnOrAfter(plugin.CreatedAt);
        plugin.LastActivityAt.Should().Be(plugin.LastHeartbeatAt);
    }

    [Fact]
    public void MarkDead_TriggersOnDeathEvent() {
        using var plugin = new TestPlugin();
        var deathCount = 0;
        plugin.OnDeath += (_, _) => deathCount++;

        plugin.MarkDead();
        deathCount.Should().Be(1);
    }

    [Fact]
    public void MarkDead_IsIdempotent() {
        using var plugin = new TestPlugin();
        var deathCount = 0;
        plugin.OnDeath += (_, _) => deathCount++;

        plugin.MarkDead();
        plugin.MarkDead();
        deathCount.Should().Be(1);
    }

    [Fact]
    public void EnsureAlive_WhenDead_Throws() {
        using var plugin = new TestPlugin();
        plugin.MarkDead();

        var act = () => plugin.EnsureAlive();
        act.Should().Throw<PluginDeadException>();
    }

    [Fact]
    public void EnsureAlive_WhenAlive_DoesNotThrow() {
        using var plugin = new TestPlugin();

        var act = () => plugin.EnsureAlive();
        act.Should().NotThrow();
    }

    [Fact]
    public void UiResources_Available() {
        using var plugin = new TestPlugin();
        plugin.UiResources.Register("toolbar.test", new UiResourceEntry("toolbar.test", UiResourceKind.ToolbarButton, "Test", null));

        plugin.UiResources.Count.Should().Be(1);
    }

    private sealed class TestSafeHandle : SafeHandleZeroOrMinusOneIsInvalid {
        public TestSafeHandle() : base(true) { }
        protected override bool ReleaseHandle() => true;
    }
}