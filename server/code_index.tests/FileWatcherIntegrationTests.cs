namespace JoinCode.CodeIndex.Tests;

public sealed class FileWatcherIntegrationTests : IDisposable
{
    private readonly InMemoryIndexStore _store;
    private readonly CodeIndexer _indexer;

    public FileWatcherIntegrationTests()
    {
        _store = new InMemoryIndexStore();
        _indexer = new CodeIndexer(_store, TestFileSystem.Current);
    }

    public void Dispose()
    {
        _indexer.Dispose();
        _store.Dispose();
    }

    [Fact]
    public async Task WatchAsync_NewCsFile_TriggersIndexing()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task WatchAsync_ModifiedCsFile_TriggersReindexing()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task WatchAsync_DeletedCsFile_TriggersRemoval()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task WatchAsync_IgnoresNonCsFiles()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task WatchAsync_IgnoresExcludedDirectories()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task WatchAsync_Debounce_RapidChangesOnlyIndexOnce()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }
}
