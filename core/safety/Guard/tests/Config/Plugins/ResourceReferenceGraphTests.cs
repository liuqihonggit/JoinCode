namespace Core.Tests.Plugins;

public sealed class ResourceReferenceGraphTests
{
    private readonly ResourceReferenceGraph _graph = new();

    [Fact]
    public void AddReference_GetConsumers_ReturnsConsumerPluginNames()
    {
        var cmdA1Id = new ObjectId(ObjectType.Resource, "cmdA1");
        var ref1 = new ResourceReference(new ObjectId(ObjectType.Resource, "cmdB1"), cmdA1Id, "pluginB", "pluginA");
        var ref2 = new ResourceReference(new ObjectId(ObjectType.Resource, "cmdC1"), cmdA1Id, "pluginC", "pluginA");

        _graph.AddReference(ref1);
        _graph.AddReference(ref2);

        var consumers = _graph.GetConsumers("pluginA");
        consumers.Should().Contain(new[] { "pluginB", "pluginC" });
        consumers.Should().HaveCount(2);
    }

    [Fact]
    public void AddReference_GetReferencesBy_ReturnsAllReferences()
    {
        var cmdA1Id = new ObjectId(ObjectType.Resource, "cmdA1");
        var cmdC1Id = new ObjectId(ObjectType.Resource, "cmdC1");
        var ref1 = new ResourceReference(new ObjectId(ObjectType.Resource, "cmdB1"), cmdA1Id, "pluginB", "pluginA");
        var ref2 = new ResourceReference(new ObjectId(ObjectType.Resource, "cmdB2"), cmdC1Id, "pluginB", "pluginC");

        _graph.AddReference(ref1);
        _graph.AddReference(ref2);

        var refs = _graph.GetReferencesBy("pluginB");
        refs.Should().HaveCount(2);
        refs.Should().Contain(ref1);
        refs.Should().Contain(ref2);
    }

    [Fact]
    public void AddReference_GetReferenceCounts_ReturnsCorrectCounts()
    {
        var cmdA1Id = new ObjectId(ObjectType.Resource, "cmdA1");
        var cmdA2Id = new ObjectId(ObjectType.Resource, "cmdA2");
        var ref1 = new ResourceReference(new ObjectId(ObjectType.Resource, "cmdB1"), cmdA1Id, "pluginB", "pluginA");
        var ref2 = new ResourceReference(new ObjectId(ObjectType.Resource, "cmdC1"), cmdA1Id, "pluginC", "pluginA");
        var ref3 = new ResourceReference(new ObjectId(ObjectType.Resource, "cmdB2"), cmdA2Id, "pluginB", "pluginA");

        _graph.AddReference(ref1);
        _graph.AddReference(ref2);
        _graph.AddReference(ref3);

        var counts = _graph.GetReferenceCounts("pluginA");
        counts.Should().HaveCount(2);
        counts[cmdA1Id].Should().Be(2);
        counts[cmdA2Id].Should().Be(1);
    }

    [Fact]
    public void RemoveReference_DecreasesCount()
    {
        var cmdA1Id = new ObjectId(ObjectType.Resource, "cmdA1");
        var cmdB1Id = new ObjectId(ObjectType.Resource, "cmdB1");
        var ref1 = new ResourceReference(cmdB1Id, cmdA1Id, "pluginB", "pluginA");
        _graph.AddReference(ref1);

        _graph.RemoveReference(ref1.ConsumerResourceId, ref1.TargetResourceId);

        var counts = _graph.GetReferenceCounts("pluginA");
        counts.Should().BeEmpty();
    }

    [Fact]
    public void RemoveAllForPlugin_RemovesAllReferences()
    {
        var ref1 = new ResourceReference(new ObjectId(ObjectType.Resource, "cmdB1"), new ObjectId(ObjectType.Resource, "cmdA1"), "pluginB", "pluginA");
        var ref2 = new ResourceReference(new ObjectId(ObjectType.Resource, "cmdA1"), new ObjectId(ObjectType.Resource, "cmdC1"), "pluginA", "pluginC");
        _graph.AddReference(ref1);
        _graph.AddReference(ref2);

        _graph.RemoveAllForPlugin("pluginA");

        _graph.GetConsumers("pluginA").Should().BeEmpty();
        _graph.GetReferencesBy("pluginA").Should().BeEmpty();
    }

    [Fact]
    public void GetConsumers_NoReferences_ReturnsEmpty()
    {
        var consumers = _graph.GetConsumers("nonexistent");
        consumers.Should().BeEmpty();
    }

    [Fact]
    public void GetConsumers_DistinctConsumerPlugins()
    {
        var cmdA1Id = new ObjectId(ObjectType.Resource, "cmdA1");
        var cmdA2Id = new ObjectId(ObjectType.Resource, "cmdA2");
        var ref1 = new ResourceReference(new ObjectId(ObjectType.Resource, "cmdB1"), cmdA1Id, "pluginB", "pluginA");
        var ref2 = new ResourceReference(new ObjectId(ObjectType.Resource, "cmdB2"), cmdA2Id, "pluginB", "pluginA");

        _graph.AddReference(ref1);
        _graph.AddReference(ref2);

        var consumers = _graph.GetConsumers("pluginA");
        consumers.Should().HaveCount(1);
        consumers.Should().Contain("pluginB");
    }

    [Fact]
    public void AddReference_DuplicateReference_NotAdded()
    {
        var cmdA1Id = new ObjectId(ObjectType.Resource, "cmdA1");
        var cmdB1Id = new ObjectId(ObjectType.Resource, "cmdB1");
        var ref1 = new ResourceReference(cmdB1Id, cmdA1Id, "pluginB", "pluginA");
        _graph.AddReference(ref1);
        _graph.AddReference(ref1);

        var counts = _graph.GetReferenceCounts("pluginA");
        counts.Values.Sum().Should().Be(1);
    }
}
