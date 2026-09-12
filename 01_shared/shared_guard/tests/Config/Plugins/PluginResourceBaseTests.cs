namespace Core.Tests.Plugins;

public sealed class PluginResourceBaseTests
{
    private sealed class TestResource : PluginResourceBase
    {
        public TestResource(string ownerPluginName, PluginResourceKind kind, string displayName)
            : base(ownerPluginName, kind, displayName) { }
    }

    [Fact]
    public void AddReference_IncrementsRefCount()
    {
        var resource = new TestResource("pluginA", PluginResourceKind.Command, "cmdA1");

        resource.ReferenceCount.Should().Be(0);
        var handle = resource.AddReference("pluginB");
        resource.ReferenceCount.Should().Be(1);

        handle.Dispose();
        resource.ReferenceCount.Should().Be(0);
        resource.Dispose();
    }

    [Fact]
    public void AddReference_MultipleConsumers_RefCountCorrect()
    {
        var resource = new TestResource("pluginA", PluginResourceKind.Command, "cmdA1");

        var h1 = resource.AddReference("pluginB");
        var h2 = resource.AddReference("pluginC");
        resource.ReferenceCount.Should().Be(2);

        h1.Dispose();
        resource.ReferenceCount.Should().Be(1);

        h2.Dispose();
        resource.ReferenceCount.Should().Be(0);
        resource.Dispose();
    }

    [Fact]
    public void ResourceReferenceHandle_DisposeIsIdempotent()
    {
        var resource = new TestResource("pluginA", PluginResourceKind.Command, "cmdA1");
        var handle = resource.AddReference("pluginB");

        handle.Dispose();
        handle.Dispose();
        resource.ReferenceCount.Should().Be(0);
        resource.Dispose();
    }

    [Fact]
    public void EnsureAlive_WhenAlive_DoesNotThrow()
    {
        var resource = new TestResource("pluginA", PluginResourceKind.Command, "cmdA1");

        var act = () => resource.EnsureAlive();
        act.Should().NotThrow();
        resource.Dispose();
    }

    [Fact]
    public void EnsureAlive_WhenDead_ThrowsPluginDeadException()
    {
        var resource = new TestResource("pluginA", PluginResourceKind.Command, "cmdA1");
        resource.MarkDead();

        var act = () => resource.EnsureAlive();
        act.Should().Throw<PluginDeadException>()
            .WithMessage("*cmdA1*pluginA*");
        resource.Dispose();
    }

    [Fact]
    public void MarkDead_TriggersOnDeathEvent()
    {
        var resource = new TestResource("pluginA", PluginResourceKind.Command, "cmdA1");
        var deathCount = 0;
        resource.OnDeath += (_, _) => deathCount++;

        resource.MarkDead();
        deathCount.Should().Be(1);
        resource.Dispose();
    }

    [Fact]
    public void MarkDead_IsIdempotent()
    {
        var resource = new TestResource("pluginA", PluginResourceKind.Command, "cmdA1");
        var deathCount = 0;
        resource.OnDeath += (_, _) => deathCount++;

        resource.MarkDead();
        resource.MarkDead();
        deathCount.Should().Be(1);
        resource.Dispose();
    }

    [Fact]
    public void Touch_UpdatesLastHeartbeatAt()
    {
        var resource = new TestResource("pluginA", PluginResourceKind.Command, "cmdA1");

        resource.Touch();
        resource.LastHeartbeatAt.Should().BeOnOrAfter(resource.CreatedAt);
        resource.LastActivityAt.Should().Be(resource.LastHeartbeatAt);
        resource.Dispose();
    }

    [Fact]
    public void GetConsumers_ReturnsAllConsumerPluginNames()
    {
        var resource = new TestResource("pluginA", PluginResourceKind.Command, "cmdA1");

        var h1 = resource.AddReference("pluginB");
        var h2 = resource.AddReference("pluginC");

        var consumers = resource.GetConsumers();
        consumers.Should().Contain(new[] { "pluginB", "pluginC" });
        consumers.Should().HaveCount(2);

        h1.Dispose();
        h2.Dispose();
        resource.Dispose();
    }

    [Fact]
    public void Dispose_UnregistersFromObjectIdManager()
    {
        var resource = new TestResource("pluginA", PluginResourceKind.Command, "cmdA1");
        var objectId = resource.ObjectId;

        ObjectIdManager.IsRegistered(objectId).Should().BeTrue();

        resource.Dispose();
        ObjectIdManager.IsRegistered(objectId).Should().BeFalse();
    }

    [Fact]
    public void Dispose_MarksDead()
    {
        var resource = new TestResource("pluginA", PluginResourceKind.Command, "cmdA1");

        resource.Dispose();
        resource.IsAlive.Should().BeFalse();
    }

    [Fact]
    public void OwnerPluginName_AndKind_Preserved()
    {
        var resource = new TestResource("pluginA", PluginResourceKind.Hook, "hookA1");

        resource.OwnerPluginName.Should().Be("pluginA");
        resource.Kind.Should().Be(PluginResourceKind.Hook);
        resource.Dispose();
    }
}
