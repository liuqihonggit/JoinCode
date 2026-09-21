namespace Core.Tests.Plugins;

public sealed class PluginResourceScannerTests {
    private sealed class TestEntity : Entity {
        public TestEntity(string displayName) : base(ObjectType.Resource, displayName: displayName) { }
        public override void Dispose() => base.Dispose();
    }

    [Fact]
    public async Task ScanPluginResources_AllUnregistered_NoLeaks() {
        var scanner = new PluginResourceScanner();
        await using var e1 = new TestEntity("res1");
        await using var e2 = new TestEntity("res2");
        var ids = new[] { e1.ObjectId, e2.ObjectId };
        await e1.DisposeAsync();
        await e2.DisposeAsync();

        var report = scanner.ScanPluginResources("pluginA", ids);

        report.HasLeaks.Should().BeFalse();
        report.LeakedResourceIds.Should().BeEmpty();
    }

    [Fact]
    public async Task ScanPluginResources_WithLeak_DetectsLeak() {
        var scanner = new PluginResourceScanner();
        await using var e1 = new TestEntity("res1");
        await using var e2 = new TestEntity("res2");
        var ids = new[] { e1.ObjectId, e2.ObjectId };
        await e1.DisposeAsync();

        var report = scanner.ScanPluginResources("pluginA", ids);

        report.HasLeaks.Should().BeTrue();
        report.LeakedResourceIds.Should().HaveCount(1);
        report.LeakedResourceIds.Should().Contain(e2.ObjectId);
    }

    [Fact]
    public void ScanPluginResources_EmptyList_NoLeaks() {
        var scanner = new PluginResourceScanner();

        var report = scanner.ScanPluginResources("pluginA", []);

        report.HasLeaks.Should().BeFalse();
    }

    [Fact]
    public void ScanPluginRecords_PluginNameInReport() {
        var scanner = new PluginResourceScanner();

        var report = scanner.ScanPluginResources("my-plugin", []);

        report.PluginName.Should().Be("my-plugin");
    }
}