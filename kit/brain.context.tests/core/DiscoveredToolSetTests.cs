namespace Core.Tests.Context.Context;

public sealed class DiscoveredToolSetTests
{
    [Fact]
    public async Task Discover_AddsToolName()
    {
        var set = new DiscoveredToolSet();
        var added = await set.DiscoverAsync("mcp.search").ConfigureAwait(true);

        Assert.True(added);
        Assert.Equal(1, await set.GetCountAsync().ConfigureAwait(true));
        Assert.True(await set.IsDiscoveredAsync("mcp.search").ConfigureAwait(true));
    }

    [Fact]
    public async Task Discover_DuplicateReturnsFalse()
    {
        var set = new DiscoveredToolSet();
        await set.DiscoverAsync("mcp.search").ConfigureAwait(true);

        var added = await set.DiscoverAsync("mcp.search").ConfigureAwait(true);

        Assert.False(added);
        Assert.Equal(1, await set.GetCountAsync().ConfigureAwait(true));
    }

    [Fact]
    public async Task DiscoverRange_AddsMultiple()
    {
        var set = new DiscoveredToolSet();
        var added = await set.DiscoverRangeAsync(["mcp.search", "mcp.read", "mcp.write"]).ConfigureAwait(true);

        Assert.Equal(3, added);
        Assert.Equal(3, await set.GetCountAsync().ConfigureAwait(true));
    }

    [Fact]
    public async Task DiscoverRange_SkipsDuplicates()
    {
        var set = new DiscoveredToolSet();
        await set.DiscoverAsync("mcp.search").ConfigureAwait(true);

        var added = await set.DiscoverRangeAsync(["mcp.search", "mcp.read"]).ConfigureAwait(true);

        Assert.Equal(1, added);
        Assert.Equal(2, await set.GetCountAsync().ConfigureAwait(true));
    }

    [Fact]
    public async Task Forget_RemovesToolName()
    {
        var set = new DiscoveredToolSet();
        await set.DiscoverAsync("mcp.search").ConfigureAwait(true);

        var removed = await set.ForgetAsync("mcp.search").ConfigureAwait(true);

        Assert.True(removed);
        Assert.Equal(0, await set.GetCountAsync().ConfigureAwait(true));
        Assert.False(await set.IsDiscoveredAsync("mcp.search").ConfigureAwait(true));
    }

    [Fact]
    public async Task Forget_NonExistentReturnsFalse()
    {
        var set = new DiscoveredToolSet();

        var removed = await set.ForgetAsync("mcp.search").ConfigureAwait(true);

        Assert.False(removed);
    }

    [Fact]
    public async Task Clear_RemovesAll()
    {
        var set = new DiscoveredToolSet();
        await set.DiscoverRangeAsync(["mcp.search", "mcp.read"]).ConfigureAwait(true);

        await set.ClearAsync().ConfigureAwait(true);

        Assert.Equal(0, await set.GetCountAsync().ConfigureAwait(true));
    }

    [Fact]
    public async Task Snapshot_ReturnsOrderedNames()
    {
        var set = new DiscoveredToolSet();
        await set.DiscoverRangeAsync(["mcp.write", "mcp.search", "mcp.read"]).ConfigureAwait(true);

        var snapshot = await set.SnapshotAsync().ConfigureAwait(true);

        Assert.Equal(["mcp.read", "mcp.search", "mcp.write"], snapshot);
    }

    [Fact]
    public async Task RestoreFromSnapshot_ReplacesAll()
    {
        var set = new DiscoveredToolSet();
        await set.DiscoverAsync("mcp.old").ConfigureAwait(true);

        await set.RestoreFromSnapshotAsync(["mcp.new1", "mcp.new2"]).ConfigureAwait(true);

        Assert.Equal(2, await set.GetCountAsync().ConfigureAwait(true));
        Assert.False(await set.IsDiscoveredAsync("mcp.old").ConfigureAwait(true));
        Assert.True(await set.IsDiscoveredAsync("mcp.new1").ConfigureAwait(true));
    }

    [Fact]
    public async Task Names_ReturnsDefensiveCopy()
    {
        var set = new DiscoveredToolSet();
        await set.DiscoverAsync("mcp.search").ConfigureAwait(true);

        var names = await set.GetNamesAsync().ConfigureAwait(true);
        await set.DiscoverAsync("mcp.read").ConfigureAwait(true);

        Assert.Single(names);
    }
}
