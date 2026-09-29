namespace JoinCode.Plugins.Tests.Unit;

/// <summary>
/// ObjectIdManager 全局状态 fixture — 确保 PluginResourceScanner 测试期间 ObjectIdManager 状态可控
/// <para>xUnit Collection 串行化,避免并行测试污染全局 ObjectIdManager</para>
/// </summary>
[CollectionDefinition("ObjectIdManager")]
public sealed class ObjectIdManagerCollection : ICollectionFixture<ObjectIdManagerFixture>;

/// <summary>
/// ObjectIdManager fixture — 每次测试 collection 开始前清空,结束后清空
/// </summary>
public sealed class ObjectIdManagerFixture : IDisposable {
    public ObjectIdManagerFixture() => ObjectIdManager.Clear();
    public void Dispose() => ObjectIdManager.Clear();
}

/// <summary>
/// PluginResourceScanner 单元测试 — 验证资源扫描泄漏检测
/// <para>确定性测试:通过 ObjectIdManager.Clear() 控制全局状态,串行化避免竞争</para>
/// </summary>
[Collection("ObjectIdManager")]
public sealed class PluginResourceScannerTest {
    private readonly PluginResourceScanner _scanner = new(null);

    [Fact]
    public void ScanPluginResources_EmptyResourceIds_ReturnsNoLeaks() {
        ObjectIdManager.Clear();
        var report = _scanner.ScanPluginResources("p", new Dictionary<ObjectType, LongRangeSet>());

        report.HasLeaks.Should().BeFalse();
        report.LeakedResourceIds.Should().BeEmpty();
        report.PluginName.Should().Be("p");
    }

    [Fact]
    public void ScanPluginResources_AllUnregistered_ReturnsNoLeaks() {
        ObjectIdManager.Clear();
        // 构造 ObjectId 但不注册到 ObjectIdManager → IsRegistered 返回 false
        var id = new ObjectId(ObjectType.Resource);
        var resourceIds = new Dictionary<ObjectType, LongRangeSet> {
            [ObjectType.Resource] = LongRangeSet.Empty.Add(id.SequenceId)
        };

        var report = _scanner.ScanPluginResources("p", resourceIds);

        report.HasLeaks.Should().BeFalse();
    }

    [Fact]
    public void ScanPluginResources_RegisteredId_ReturnsAsLeak() {
        ObjectIdManager.Clear();
        var id = new ObjectId(ObjectType.Resource);
        ObjectIdManager.Register(new object(), id);
        var resourceIds = new Dictionary<ObjectType, LongRangeSet> {
            [ObjectType.Resource] = LongRangeSet.Empty.Add(id.SequenceId)
        };

        var report = _scanner.ScanPluginResources("p", resourceIds);

        report.HasLeaks.Should().BeTrue();
        report.LeakedResourceIds.Should().HaveCount(1);
        report.LeakedResourceIds[0].Should().Be(id);
    }

    [Fact]
    public void ScanPluginResources_MixedRegisteredAndUnregistered_OnlyReportsRegistered() {
        ObjectIdManager.Clear();
        var id1 = new ObjectId(ObjectType.Resource);
        var id2 = new ObjectId(ObjectType.Resource);
        var id3 = new ObjectId(ObjectType.Resource);
        ObjectIdManager.Register(new object(), id2); // 仅 id2 注册

        var resourceIds = new Dictionary<ObjectType, LongRangeSet> {
            [ObjectType.Resource] = LongRangeSet.Empty.Add(id1.SequenceId).Add(id2.SequenceId).Add(id3.SequenceId)
        };

        var report = _scanner.ScanPluginResources("p", resourceIds);

        report.HasLeaks.Should().BeTrue();
        report.LeakedResourceIds.Should().HaveCount(1);
        report.LeakedResourceIds[0].Should().Be(id2);
    }

    [Fact]
    public void ScanPluginResources_EmptyLongRangeSet_Skipped() {
        ObjectIdManager.Clear();
        var resourceIds = new Dictionary<ObjectType, LongRangeSet> {
            [ObjectType.Resource] = LongRangeSet.Empty // 空区间
        };

        var report = _scanner.ScanPluginResources("p", resourceIds);

        report.HasLeaks.Should().BeFalse();
    }

    [Fact]
    public void ScanPluginResources_MultipleObjectTypes_ScansAll() {
        ObjectIdManager.Clear();
        var resourceId = new ObjectId(ObjectType.Resource);
        var pluginId = new ObjectId(ObjectType.Plugin);
        ObjectIdManager.Register(new object(), resourceId);
        ObjectIdManager.Register(new object(), pluginId);

        var resourceIds = new Dictionary<ObjectType, LongRangeSet> {
            [ObjectType.Resource] = LongRangeSet.Empty.Add(resourceId.SequenceId),
            [ObjectType.Plugin] = LongRangeSet.Empty.Add(pluginId.SequenceId)
        };

        var report = _scanner.ScanPluginResources("p", resourceIds);

        report.HasLeaks.Should().BeTrue();
        report.LeakedResourceIds.Should().HaveCount(2);
    }

    [Fact]
    public void ScanPluginResources_NullPluginName_Throws() {
        Action act = () => _scanner.ScanPluginResources(null!, new Dictionary<ObjectType, LongRangeSet>());
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void ScanPluginResources_NullResourceIds_Throws() {
        Action act = () => _scanner.ScanPluginResources("p", null!);
        act.Should().Throw<ArgumentNullException>();
    }
}
