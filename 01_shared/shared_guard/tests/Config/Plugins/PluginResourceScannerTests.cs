namespace Core.Tests.Plugins;

public sealed class PluginResourceScannerTests
{
    private sealed class TestEntity : Entity
    {
        public TestEntity(string displayName) : base(ObjectType.Resource, displayName: displayName) { }
        protected override void OnDispose() { }
    }

    [Fact]
    public void ScanPluginResources_AllUnregistered_NoLeaks()
    {
        var scanner = new PluginResourceScanner();
        var e1 = new TestEntity("res1");
        var e2 = new TestEntity("res2");
        var ids = new[] { e1.ObjectId, e2.ObjectId };
        e1.Dispose();
        e2.Dispose();

        var report = scanner.ScanPluginResources("pluginA", ids);

        report.HasLeaks.Should().BeFalse();
        report.LeakedResourceIds.Should().BeEmpty();
    }

    [Fact]
    public void ScanPluginResources_WithLeak_DetectsLeak()
    {
        var scanner = new PluginResourceScanner();
        var e1 = new TestEntity("res1");
        var e2 = new TestEntity("res2");
        var ids = new[] { e1.ObjectId, e2.ObjectId };
        e1.Dispose();

        var report = scanner.ScanPluginResources("pluginA", ids);

        report.HasLeaks.Should().BeTrue();
        report.LeakedResourceIds.Should().HaveCount(1);
        report.LeakedResourceIds.Should().Contain(e2.ObjectId);
        e2.Dispose();
    }

    [Fact]
    public void ScanPluginResources_EmptyList_NoLeaks()
    {
        var scanner = new PluginResourceScanner();

        var report = scanner.ScanPluginResources("pluginA", []);

        report.HasLeaks.Should().BeFalse();
    }

    [Fact]
    public void ScanPluginRecords_PluginNameInReport()
    {
        var scanner = new PluginResourceScanner();

        var report = scanner.ScanPluginResources("my-plugin", []);

        report.PluginName.Should().Be("my-plugin");
    }
}
