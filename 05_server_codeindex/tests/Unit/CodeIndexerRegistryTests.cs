namespace JoinCode.CodeIndex.Tests;

public sealed class CodeIndexerRegistryTests : IDisposable
{
    private readonly InMemoryIndexStore _defaultStore;
    private readonly CodeIndexer _defaultIndexer;
    private readonly IFileSystem _fs;
    private readonly CodeIndexerRegistry _registry;

    public CodeIndexerRegistryTests()
    {
        _defaultStore = new InMemoryIndexStore();
        _fs = new IO.FileSystem.InMemoryFileSystem();
        _defaultIndexer = new CodeIndexer(_defaultStore, _fs);
        _registry = new CodeIndexerRegistry(_fs, _defaultIndexer);
    }

    public void Dispose()
    {
        _registry.Dispose();
    }

    [Fact]
    public void Constructor_NullFileSystem_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CodeIndexerRegistry(null!, _defaultIndexer));
    }

    [Fact]
    public void Constructor_NullDefaultIndexer_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new CodeIndexerRegistry(_fs, null!));
    }

    [Fact]
    public void DefaultIndexer_ReturnsInjectedIndexer()
    {
        Assert.Same(_defaultIndexer, _registry.DefaultIndexer);
    }

    [Fact]
    public async Task RegisterAsync_ValidRepo_ReturnsRegistration()
    {
        var reg = await _registry.RegisterAsync("repo1", "/workspace/repo1", CancellationToken.None);

        Assert.Equal("repo1", reg.RepoId);
        Assert.Equal("/workspace/repo1", reg.WorkspaceRoot);
        Assert.False(reg.IsDefault);
        Assert.False(reg.IsWatching);
        Assert.True(reg.RegisteredAt <= DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task RegisterAsync_DuplicateRepo_Throws()
    {
        await _registry.RegisterAsync("repo1", "/workspace/repo1", CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => _registry.RegisterAsync("repo1", "/workspace/repo1", CancellationToken.None));
    }

    [Fact]
    public async Task RegisterAsync_NullRepoId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _registry.RegisterAsync(null!, "/workspace", CancellationToken.None));
    }

    [Fact]
    public async Task RegisterAsync_NullWorkspaceRoot_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _registry.RegisterAsync("repo1", null!, CancellationToken.None));
    }

    [Fact]
    public async Task GetIndexer_Default_ReturnsDefaultIndexer()
    {
        var indexer = _registry.GetIndexer("default");
        Assert.Same(_defaultIndexer, indexer);
    }

    [Fact]
    public async Task GetIndexer_RegisteredRepo_ReturnsIndexer()
    {
        await _registry.RegisterAsync("repo1", "/workspace/repo1", CancellationToken.None);

        var indexer = _registry.GetIndexer("repo1");
        Assert.NotNull(indexer);
        Assert.NotSame(_defaultIndexer, indexer);
    }

    [Fact]
    public async Task GetIndexer_UnknownRepo_ReturnsNull()
    {
        var indexer = _registry.GetIndexer("nonexistent");
        Assert.Null(indexer);
    }

    [Fact]
    public async Task GetIndexer_NullRepoId_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => _registry.GetIndexer(null!));
    }

    [Fact]
    public async Task UnregisterAsync_ExistingRepo_ReturnsTrue()
    {
        await _registry.RegisterAsync("repo1", "/workspace/repo1", CancellationToken.None);

        var result = await _registry.UnregisterAsync("repo1", CancellationToken.None);
        Assert.True(result);

        var indexer = _registry.GetIndexer("repo1");
        Assert.Null(indexer);
    }

    [Fact]
    public async Task UnregisterAsync_NonExistingRepo_ReturnsFalse()
    {
        var result = await _registry.UnregisterAsync("nonexistent", CancellationToken.None);
        Assert.False(result);
    }

    [Fact]
    public async Task UnregisterAsync_NullRepoId_Throws()
    {
        await Assert.ThrowsAsync<ArgumentNullException>(
            () => _registry.UnregisterAsync(null!, CancellationToken.None));
    }

    [Fact]
    public async Task ListReposAsync_NoRepos_ReturnsOnlyDefault()
    {
        var repos = await _registry.ListReposAsync(CancellationToken.None);

        Assert.Single(repos);
        Assert.True(repos[0].IsDefault);
        Assert.Equal("default", repos[0].RepoId);
    }

    [Fact]
    public async Task ListReposAsync_WithRepos_ReturnsDefaultAndRegistered()
    {
        await _registry.RegisterAsync("repo1", "/workspace/repo1", CancellationToken.None);
        await _registry.RegisterAsync("repo2", "/workspace/repo2", CancellationToken.None);

        var repos = await _registry.ListReposAsync(CancellationToken.None);

        Assert.Equal(3, repos.Count);
        Assert.Single(repos, r => r.IsDefault);
        Assert.Single(repos, r => r.RepoId == "repo1");
        Assert.Single(repos, r => r.RepoId == "repo2");
    }

    [Fact]
    public async Task ListReposAsync_AfterUnregister_ExcludesUnregisteredRepo()
    {
        await _registry.RegisterAsync("repo1", "/workspace/repo1", CancellationToken.None);
        await _registry.RegisterAsync("repo2", "/workspace/repo2", CancellationToken.None);
        await _registry.UnregisterAsync("repo1", CancellationToken.None);

        var repos = await _registry.ListReposAsync(CancellationToken.None);

        Assert.Equal(2, repos.Count);
        Assert.Single(repos, r => r.IsDefault);
        Assert.Single(repos, r => r.RepoId == "repo2");
    }

    [Fact]
    public async Task RegisterAsync_MultipleRepos_EachHasIndependentIndexer()
    {
        await _registry.RegisterAsync("repo1", "/workspace/repo1", CancellationToken.None);
        await _registry.RegisterAsync("repo2", "/workspace/repo2", CancellationToken.None);

        var indexer1 = _registry.GetIndexer("repo1");
        var indexer2 = _registry.GetIndexer("repo2");

        Assert.NotNull(indexer1);
        Assert.NotNull(indexer2);
        Assert.NotSame(indexer1, indexer2);
        Assert.NotSame(indexer1, _defaultIndexer);
        Assert.NotSame(indexer2, _defaultIndexer);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow()
    {
        _registry.Dispose();
        _registry.Dispose();
    }

    [Fact]
    public async Task Dispose_DisposesAllRegisteredIndexers()
    {
        await _registry.RegisterAsync("repo1", "/workspace/repo1", CancellationToken.None);
        await _registry.RegisterAsync("repo2", "/workspace/repo2", CancellationToken.None);

        _registry.Dispose();

        Assert.Throws<ObjectDisposedException>(() => _registry.GetIndexer("repo1"));
    }
}
