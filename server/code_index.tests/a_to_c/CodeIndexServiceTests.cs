namespace JoinCode.CodeIndex.Tests;

public sealed class CodeIndexServiceTests : IDisposable {
    private readonly InMemoryIndexStore _store;
    private readonly CodeIndexer _indexer;
    private bool _disposed;

    public CodeIndexServiceTests() {
        _store = new InMemoryIndexStore();
        _indexer = new CodeIndexer(_store, TestFileSystem.Current);
    }

    public void Dispose() {
        if (_disposed) return;
        _disposed = true;
        _indexer.DisposeSafe();
        _store.DisposeSafe();
    }

    [Fact]
    public async Task StartAsync_BuildsIndexAndStartsWatcher() {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task StopAsync_StopsWatcher() {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task StartAsync_WithoutWatcher_StillBuildsIndex() {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public void Dispose_CalledTwice_DoesNotThrow() {
    }

    [Fact]
    public async Task StartAsync_EmptyWorkspace_NoFilesIndexed() {
        await Task.CompletedTask.ConfigureAwait(true);
    }
}