namespace Core.Tests.Plugins;

/// <summary>
/// 跨插件链路验证 — 修复断裂点 #3(AddReference)、#4(EnsureAlive)、#8(PrepareUnloadAsync)
/// </summary>
public sealed class CrossPluginLinkTests {
    private sealed class PluginA : WorkflowPluginBase {
        public override string Name => "pluginA";
        public override string Version => "1.0.0";
        public override string Description => "Test Plugin A";
        public override Task<OperationResult> LoadAsync(PluginContext ctx, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Ok());
        public override Task<OperationResult> InitializeAsync(IServiceProvider sp, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok());

        public PluginA() : base("pluginA") { }

        public CommandResourceA CreateCommandResource() {
            var resource = new CommandResourceA(Name, "cmdA");
            RegisterResource(resource);
            return resource;
        }
    }

    private sealed class PluginB : WorkflowPluginBase {
        public override string Name => "pluginB";
        public override string Version => "1.0.0";
        public override string Description => "Test Plugin B";
        public override Task<OperationResult> LoadAsync(PluginContext ctx, CancellationToken cancellationToken = default) => Task.FromResult(OperationResult.Ok());
        public override Task<OperationResult> InitializeAsync(IServiceProvider sp, CancellationToken ct = default) => Task.FromResult(OperationResult.Ok());

        public PluginB() : base("pluginB") { }

        public new T RegisterResource<T>(T resource) where T : PluginResourceBase => base.RegisterResource(resource);
    }

    private sealed class CommandResourceA : PluginResourceBase {
        public CommandResourceA(string owner, string name) : base(owner, PluginResourceKind.Command, name) { }
    }

    [Fact]
    public async Task AddReference_CrossPlugin_RefCountIncremented() {
        await using var pluginA = new PluginA();
        await using var pluginB = new PluginB();
        var cmdA = pluginA.CreateCommandResource();

        var handle = cmdA.AddReference(pluginB.Name);
        cmdA.ReferenceCount.Should().Be(1);
        cmdA.GetConsumers().Should().Contain("pluginB");

        handle.Dispose();
        cmdA.ReferenceCount.Should().Be(0);
    }

    [Fact]
    public async Task EnsureAlive_CrossPlugin_DetectsProviderDeath() {
        await using var pluginA = new PluginA();
        await using var pluginB = new PluginB();
        var cmdA = pluginA.CreateCommandResource();
        var handle = cmdA.AddReference(pluginB.Name);

        var act1 = () => cmdA.EnsureAlive();
        act1.Should().NotThrow();

        pluginA.MarkDead();
        cmdA.MarkDead();

        var act2 = () => cmdA.EnsureAlive();
        act2.Should().Throw<PluginDeadException>().WithMessage("*cmdA*pluginA*");

        handle.Dispose();
    }

    [Fact]
    public async Task PrepareUnload_ReferenceGraph_ConsumersNotified() {
        var graph = new ResourceReferenceGraph();
        await using var pluginA = new PluginA();
        await using var pluginB = new PluginB();
        var cmdA = pluginA.CreateCommandResource();
        await using var cmdB = new CommandResourceA(pluginB.Name, "cmdB");
        pluginB.RegisterResource(cmdB);

        var reference = new ResourceReference(
            cmdB.ObjectId,
            cmdA.ObjectId,
            pluginB.Name,
            pluginA.Name);
        graph.AddReference(reference);

        var consumers = graph.GetConsumers(pluginA.Name);
        consumers.Should().Contain("pluginB");

        var refsByB = graph.GetReferencesBy(pluginB.Name);
        refsByB.Should().HaveCount(1);

        foreach (var r in refsByB.Where(r => string.Equals(r.TargetPluginName, pluginA.Name, StringComparison.OrdinalIgnoreCase))) {
            graph.RemoveReference(r.ConsumerResourceId, r.TargetResourceId);
        }

        graph.GetConsumers(pluginA.Name).Should().BeEmpty();
        cmdA.ReferenceCount.Should().Be(0);
    }

    [Fact]
    public async Task TwoPhaseUnload_ResourceIdsCollectedAndScanned() {
        await using var pluginA = new PluginA();
        var cmdA = pluginA.CreateCommandResource();
        var resourceIds = pluginA.Resources.Select(r => r.ObjectId).ToList();
        resourceIds.Should().HaveCount(1);

        var scanner = new PluginResourceScanner();
        ObjectIdManager.IsRegistered(resourceIds[0]).Should().BeTrue();

        await pluginA.UnloadAsync();

        var report = scanner.ScanPluginResources(pluginA.Name, resourceIds);
        report.HasLeaks.Should().BeFalse();
    }

    [Fact]
    public async Task PluginDeath_CascadesToDependents() {
        await using var pluginA = new PluginA();
        await using var pluginB = new PluginB();
        var cmdA = pluginA.CreateCommandResource();
        var handle = cmdA.AddReference(pluginB.Name);

        var bDeathCount = 0;
        pluginB.OnDeath += (_, _) => bDeathCount++;

        cmdA.OnDeath += (_, _) => {
            pluginB.MarkDead();
        };

        pluginA.MarkDead();
        cmdA.MarkDead();

        bDeathCount.Should().Be(1);
        pluginB.IsAlive.Should().BeFalse();

        handle.Dispose();
    }

    [Fact]
    public async Task ResourceReferenceHandle_UsingPattern_AutoRelease() {
        await using var pluginA = new PluginA();
        await using var pluginB = new PluginB();
        var cmdA = pluginA.CreateCommandResource();

        using (cmdA.AddReference(pluginB.Name)) {
            cmdA.ReferenceCount.Should().Be(1);
        }

        cmdA.ReferenceCount.Should().Be(0);
    }
}