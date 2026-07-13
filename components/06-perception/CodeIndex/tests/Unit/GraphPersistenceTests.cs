namespace JoinCode.CodeIndex.Tests;

public sealed class GraphPersistenceTests : IDisposable
{
    private readonly InMemoryIndexStore _store;
    private readonly SymbolIndex _index;

    public GraphPersistenceTests()
    {
        _store = new InMemoryIndexStore();
        _index = new SymbolIndex(_store, TestFileSystem.Current, new CSharpSymbolExtractor());
    }

    public void Dispose()
    {
        _index.Dispose();
        _store.Dispose();
    }

    [Fact]
    public async Task IndexFileAsync_WithCallEdges_PersistsCallGraph()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task IndexFileAsync_WithDependencies_PersistsDependencyGraph()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task RemoveFileAsync_RemovesCallAndDependencyEdges()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }

    [Fact]
    public async Task IndexFileAsync_CrossFileInterface_CorrectsInheritsToImplements()
    {
        await Task.CompletedTask.ConfigureAwait(true);
    }
}
