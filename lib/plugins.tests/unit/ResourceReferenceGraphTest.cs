namespace JoinCode.Plugins.Tests.Unit;

/// <summary>
/// ResourceReferenceGraph 单元测试 — 验证三字典(引用/按消费者/按目标)协同更新
/// <para>确定性测试:不依赖时序/IO,纯字典操作</para>
/// </summary>
public sealed class ResourceReferenceGraphTest {
    private static ResourceReference MakeRef(
        string consumerPlugin, string targetPlugin,
        ObjectType type = ObjectType.Resource) {
        var consumerId = new ObjectId(type);
        var targetId = new ObjectId(type);
        return new ResourceReference(consumerId, targetId, consumerPlugin, targetPlugin);
    }

    [Fact]
    public void AddReference_UpdatesAllThreeDictionaries() {
        var graph = new ResourceReferenceGraph();
        var ref1 = MakeRef("B", "A");

        graph.AddReference(ref1);

        graph.GetConsumers("A").Should().Contain("B");
        graph.GetReferencesBy("B").Should().HaveCount(1);
        graph.GetReferenceCounts("A").Should().HaveCount(1);
    }

    [Fact]
    public void AddReference_DuplicateKey_NoDoubleAdd() {
        var graph = new ResourceReferenceGraph();
        var ref1 = MakeRef("B", "A");
        // 相同 ConsumerResourceId+TargetResourceId 的引用不应重复添加
        var ref1Dup = new ResourceReference(ref1.ConsumerResourceId, ref1.TargetResourceId, "B", "A");

        graph.AddReference(ref1);
        graph.AddReference(ref1Dup);

        graph.GetReferencesBy("B").Should().HaveCount(1);
    }

    [Fact]
    public void RemoveReference_UpdatesAllThreeDictionaries() {
        var graph = new ResourceReferenceGraph();
        var ref1 = MakeRef("B", "A");
        graph.AddReference(ref1);

        graph.RemoveReference(ref1.ConsumerResourceId, ref1.TargetResourceId);

        graph.GetConsumers("A").Should().BeEmpty();
        graph.GetReferencesBy("B").Should().BeEmpty();
        graph.GetReferenceCounts("A").Should().BeEmpty();
    }

    [Fact]
    public void RemoveReference_NonExistent_NoOp() {
        var graph = new ResourceReferenceGraph();
        var id1 = new ObjectId(ObjectType.Resource);
        var id2 = new ObjectId(ObjectType.Resource);

        // 不抛异常即可
        graph.RemoveReference(id1, id2);
    }

    [Fact]
    public void GetConsumers_MultipleConsumers_ReturnsDistinct() {
        var graph = new ResourceReferenceGraph();
        // 两个不同的 B 资源引用同一个 A 资源
        var targetId = new ObjectId(ObjectType.Resource);
        var ref1 = new ResourceReference(new ObjectId(ObjectType.Resource), targetId, "B", "A");
        var ref2 = new ResourceReference(new ObjectId(ObjectType.Resource), targetId, "C", "A");
        graph.AddReference(ref1);
        graph.AddReference(ref2);

        var consumers = graph.GetConsumers("A");
        consumers.Should().HaveCount(2);
        consumers.Should().Contain(["B", "C"]);
    }

    [Fact]
    public void GetConsumers_NoConsumers_ReturnsEmpty() {
        var graph = new ResourceReferenceGraph();

        graph.GetConsumers("nonexistent").Should().BeEmpty();
    }

    [Fact]
    public void GetReferencesBy_ReturnsAllReferencesForConsumer() {
        var graph = new ResourceReferenceGraph();
        var ref1 = MakeRef("B", "A");
        var ref2 = MakeRef("B", "C");
        graph.AddReference(ref1);
        graph.AddReference(ref2);

        var refs = graph.GetReferencesBy("B");
        refs.Should().HaveCount(2);
    }

    [Fact]
    public void GetReferencesBy_NoReferences_ReturnsEmpty() {
        var graph = new ResourceReferenceGraph();

        graph.GetReferencesBy("nonexistent").Should().BeEmpty();
    }

    [Fact]
    public void GetReferenceCounts_GroupsByTargetResourceId() {
        var graph = new ResourceReferenceGraph();
        // 同一个 target 资源被两个 consumer 引用
        var targetId = new ObjectId(ObjectType.Resource);
        var ref1 = new ResourceReference(new ObjectId(ObjectType.Resource), targetId, "B", "A");
        var ref2 = new ResourceReference(new ObjectId(ObjectType.Resource), targetId, "C", "A");
        graph.AddReference(ref1);
        graph.AddReference(ref2);

        var counts = graph.GetReferenceCounts("A");
        counts.Should().HaveCount(1);
        counts[targetId].Should().Be(2);
    }

    [Fact]
    public void GetReferenceCounts_NoReferences_ReturnsEmpty() {
        var graph = new ResourceReferenceGraph();

        graph.GetReferenceCounts("nonexistent").Should().BeEmpty();
    }

    [Fact]
    public void RemoveAllForPlugin_RemovesConsumerReferences() {
        var graph = new ResourceReferenceGraph();
        var ref1 = MakeRef("B", "A");
        var ref2 = MakeRef("B", "C");
        graph.AddReference(ref1);
        graph.AddReference(ref2);

        graph.RemoveAllForPlugin("B");

        graph.GetReferencesBy("B").Should().BeEmpty();
    }

    [Fact]
    public void RemoveAllForPlugin_RemovesTargetReferences() {
        var graph = new ResourceReferenceGraph();
        var ref1 = MakeRef("B", "A");
        graph.AddReference(ref1);

        graph.RemoveAllForPlugin("A");

        graph.GetConsumers("A").Should().BeEmpty();
        graph.GetReferenceCounts("A").Should().BeEmpty();
    }

    [Fact]
    public void RemoveAllForPlugin_NonExistent_NoOp() {
        var graph = new ResourceReferenceGraph();
        var ref1 = MakeRef("B", "A");
        graph.AddReference(ref1);

        graph.RemoveAllForPlugin("nonexistent");

        // 原有引用不受影响
        graph.GetReferencesBy("B").Should().HaveCount(1);
    }
}
