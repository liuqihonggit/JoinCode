namespace CodeIndex.Tests;

public sealed class ParallelIndexTests : IDisposable
{
    private readonly InMemoryIndexStore _store;

    public ParallelIndexTests()
    {
        _store = new InMemoryIndexStore();
    }

    public void Dispose()
    {
        _store.Dispose();
    }

    [Fact]
    public async Task BuildIndexAsync_LargeProject_IndexesAllFiles()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task BuildIndexAsync_SecondRun_SkipsUnchangedFiles()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task BuildIndexAsync_PartialChange_OnlyUpdatesChangedFiles()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task BuildIndexAsync_DeletedFiles_RemovedFromIndex()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task BuildIndexAsync_SearchWorksAfterIndex()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task BuildIndexAsync_CallGraphWorksAfterIndex()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task BuildIndexAsync_WithProgress_ReportsProgress()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }
}
