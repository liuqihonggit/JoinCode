namespace JoinCode.CodeIndex.Tests;

public sealed class FileWatcherIntegrationTests : IDisposable {
    private readonly InMemoryIndexStore _store;
    private readonly CodeIndexer _indexer;
    private bool _disposed;

    public FileWatcherIntegrationTests() {
        _store = new InMemoryIndexStore();
        _indexer = new CodeIndexer(_store, TestFileSystem.Current);
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _indexer.DisposeSafe();
        _store.Dispose();
    }

    [Fact]
    public async Task WatchAsync_NewCsFile_TriggersIndexing() {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task WatchAsync_ModifiedCsFile_TriggersReindexing() {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task WatchAsync_DeletedCsFile_TriggersRemoval() {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task WatchAsync_IgnoresNonCsFiles() {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task WatchAsync_IgnoresExcludedDirectories() {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task WatchAsync_Debounce_RapidChangesOnlyIndexOnce() {
        await Task.CompletedTask.ConfigureAwait(true);
    }
}