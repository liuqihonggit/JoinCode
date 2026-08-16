namespace Core.Tests.Plugins;

public sealed class UiResourceTableTests
{
    [Fact]
    public void Register_AddsResource()
    {
        var table = new UiResourceTable();
        var entry = new UiResourceEntry("toolbar.dream", UiResourceKind.ToolbarButton, "Dream", null);

        table.Register("toolbar.dream", entry);
        table.Count.Should().Be(1);
        table.GetAll().Should().Contain(entry);
    }

    [Fact]
    public void Unregister_RemovesResource()
    {
        var table = new UiResourceTable();
        var entry = new UiResourceEntry("menu.dream", UiResourceKind.MenuItem, "Dream", null);
        table.Register("menu.dream", entry);

        var result = table.Unregister("menu.dream");
        result.Should().BeTrue();
        table.Count.Should().Be(0);
    }

    [Fact]
    public void TryGet_ReturnsEntry()
    {
        var table = new UiResourceTable();
        var entry = new UiResourceEntry("icon.dream", UiResourceKind.Icon, "Dream", "path");
        table.Register("icon.dream", entry);

        table.TryGet("icon.dream", out var found).Should().BeTrue();
        found.Should().Be(entry);
    }

    [Fact]
    public void ClearAndEmitEvent_ReturnsAllResourcesAndClears()
    {
        var table = new UiResourceTable();
        table.Register("a", new UiResourceEntry("a", UiResourceKind.Icon, "A", null));
        table.Register("b", new UiResourceEntry("b", UiResourceKind.MenuItem, "B", null));

        var evt = table.ClearAndEmitEvent("pluginA");
        evt.PluginName.Should().Be("pluginA");
        evt.RemovedResources.Should().HaveCount(2);
        table.Count.Should().Be(0);
    }
}

public sealed class UnmanagedResourceTableTests
{
    private sealed class TestSafeHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public TestSafeHandle() : base(true) { }
        protected override bool ReleaseHandle() => true;
    }

    [Fact]
    public void Register_AddsResource()
    {
        var table = new UnmanagedResourceTable();
        var handle = new TestSafeHandle();

        var registration = table.Register("buffer1", handle, 1024);
        table.Count.Should().Be(1);
        table.TotalEstimatedBytes.Should().Be(1024);

        registration.Dispose();
        table.Count.Should().Be(0);
    }

    [Fact]
    public void GetAll_ReturnsAllEntries()
    {
        var table = new UnmanagedResourceTable();
        table.Register("buf1", new TestSafeHandle(), 100);
        table.Register("buf2", new TestSafeHandle(), 200);

        var all = table.GetAll();
        all.Should().HaveCount(2);
        table.TotalEstimatedBytes.Should().Be(300);
    }

    [Fact]
    public void ReleaseAll_ReleasesAllHandles()
    {
        var table = new UnmanagedResourceTable();
        var h1 = new TestSafeHandle();
        var h2 = new TestSafeHandle();
        table.Register("buf1", h1, 100);
        table.Register("buf2", h2, 200);

        table.ReleaseAll();
        table.Count.Should().Be(0);
        h1.IsClosed.Should().BeTrue();
        h2.IsClosed.Should().BeTrue();
    }

    [Fact]
    public void UnmanagedResourceHandle_DisposeIsIdempotent()
    {
        var table = new UnmanagedResourceTable();
        var handle = new TestSafeHandle();
        var registration = table.Register("buf1", handle, 100);

        registration.Dispose();
        registration.Dispose();
        table.Count.Should().Be(0);
    }

    [Fact]
    public void TryGet_ReturnsEntry()
    {
        var table = new UnmanagedResourceTable();
        var handle = new TestSafeHandle();
        table.Register("buf1", handle, 512);

        table.TryGet("buf1", out var entry).Should().BeTrue();
        entry!.Key.Should().Be("buf1");
        entry.EstimatedBytes.Should().Be(512);
    }
}
