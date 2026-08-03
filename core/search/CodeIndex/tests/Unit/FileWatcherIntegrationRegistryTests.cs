namespace JoinCode.CodeIndex.Tests;

public sealed class FileWatcherIntegrationRegistryTests : IAsyncDisposable
{
    private readonly InMemoryIndexStore _defaultStore;
    private readonly CodeIndexer _defaultIndexer;
    private readonly IO.FileSystem.InMemoryFileSystem _fs;
    private readonly CodeIndexerRegistry _registry;
    private readonly FileWatcherIntegrationRegistry _watcherRegistry;

    public FileWatcherIntegrationRegistryTests()
    {
        _defaultStore = new InMemoryIndexStore();
        _fs = new IO.FileSystem.InMemoryFileSystem();
        _defaultIndexer = new CodeIndexer(_defaultStore, _fs);
        _registry = new CodeIndexerRegistry(_fs, _defaultIndexer);
        _watcherRegistry = new FileWatcherIntegrationRegistry(_registry, _fs);
    }

    public async ValueTask DisposeAsync()
    {
        await _watcherRegistry.DisposeAsync();
        _registry.Dispose();
    }

    [Fact]
    public async Task RegisterRepo_WatcherIsRunning()
    {
        await _registry.RegisterAsync("repo1", "/workspace/repo1", CancellationToken.None);

        Assert.True(_watcherRegistry.IsWatching("repo1"));
    }

    [Fact]
    public async Task UnregisterRepo_WatcherIsStopped()
    {
        await _registry.RegisterAsync("repo1", "/workspace/repo1", CancellationToken.None);
        Assert.True(_watcherRegistry.IsWatching("repo1"));

        await _registry.UnregisterAsync("repo1", CancellationToken.None);

        Assert.False(_watcherRegistry.IsWatching("repo1"));
    }

    [Fact]
    public async Task MultipleRepos_EachHasIndependentWatcher()
    {
        await _registry.RegisterAsync("repo1", "/workspace/repo1", CancellationToken.None);
        await _registry.RegisterAsync("repo2", "/workspace/repo2", CancellationToken.None);

        Assert.True(_watcherRegistry.IsWatching("repo1"));
        Assert.True(_watcherRegistry.IsWatching("repo2"));

        var watchingIds = _watcherRegistry.GetWatchingRepoIds();
        Assert.Equal(2, watchingIds.Count());
        Assert.Contains("repo1", watchingIds);
        Assert.Contains("repo2", watchingIds);
    }

    [Fact]
    public async Task UnregisterOneRepo_OtherWatchersStillRunning()
    {
        await _registry.RegisterAsync("repo1", "/workspace/repo1", CancellationToken.None);
        await _registry.RegisterAsync("repo2", "/workspace/repo2", CancellationToken.None);

        await _registry.UnregisterAsync("repo1", CancellationToken.None);

        Assert.False(_watcherRegistry.IsWatching("repo1"));
        Assert.True(_watcherRegistry.IsWatching("repo2"));
    }

    [Fact]
    public async Task IsWatching_UnknownRepo_ReturnsFalse()
    {
        Assert.False(_watcherRegistry.IsWatching("nonexistent"));
    }

    [Fact]
    public async Task GetWatchingRepoIds_NoRepos_ReturnsEmpty()
    {
        var ids = _watcherRegistry.GetWatchingRepoIds();
        Assert.Empty(ids);
    }

    [Fact]
    public async Task DisposeAsync_StopsAllWatchers()
    {
        await _registry.RegisterAsync("repo1", "/workspace/repo1", CancellationToken.None);
        await _registry.RegisterAsync("repo2", "/workspace/repo2", CancellationToken.None);

        await _watcherRegistry.DisposeAsync();

        Assert.False(_watcherRegistry.IsWatching("repo1"));
        Assert.False(_watcherRegistry.IsWatching("repo2"));
    }

    [Fact]
    public async Task DisposeAsync_CalledTwice_DoesNotThrow()
    {
        await _watcherRegistry.DisposeAsync();
        await _watcherRegistry.DisposeAsync();
    }

    [Fact]
    public async Task AfterDispose_NewRegistrationDoesNotStartWatcher()
    {
        await _watcherRegistry.DisposeAsync();

        await _registry.RegisterAsync("repo1", "/workspace/repo1", CancellationToken.None);

        Assert.False(_watcherRegistry.IsWatching("repo1"));
    }
}
