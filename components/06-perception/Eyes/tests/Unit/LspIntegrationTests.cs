namespace CodeIndex.Tests;

public sealed class LspIntegrationTests
{
    private readonly Mock<ICodeIndexer> _mockIndexer;
    private readonly Mock<ILspService> _mockLspService;
    private readonly ICodeIndexer _indexer;
    private readonly ILspService _lspService;

    public LspIntegrationTests()
    {
        _mockIndexer = new Mock<ICodeIndexer>();
        _mockLspService = new Mock<ILspService>();

        _mockIndexer.SetupGet(x => x.Searcher).Returns(Mock.Of<ISymbolSearcher>());
        _mockIndexer.SetupGet(x => x.CallGraph).Returns(Mock.Of<ICallGraph>());
        _mockIndexer.SetupGet(x => x.DependencyGraph).Returns(Mock.Of<IDependencyGraph>());

        _indexer = _mockIndexer.Object;
        _lspService = _mockLspService.Object;
    }

    [Fact]
    public void Constructor_WithNullIndexer_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new LspIntegration(null!));
    }

    [Fact]
    public void Constructor_WithLspService_SetsIsLspAvailableTrue()
    {
        var integration = new LspIntegration(_indexer, _lspService);

        Assert.True(integration.IsLspAvailable);

        integration.Dispose();
    }

    [Fact]
    public void Constructor_WithoutLspService_SetsIsLspAvailableFalse()
    {
        var integration = new LspIntegration(_indexer);

        Assert.False(integration.IsLspAvailable);

        integration.Dispose();
    }

    [Fact]
    public async Task OnDocumentChangedAsync_TriggersIndexerUpdate()
    {
        var integration = new LspIntegration(_indexer, _lspService);
        var filePath = "/src/Service.cs";

        await integration.OnDocumentChangedAsync(filePath, CancellationToken.None).ConfigureAwait(true);

        _mockIndexer.Verify(x => x.UpdateFileAsync(filePath, CancellationToken.None), Times.Once);

        integration.Dispose();
    }

    [Fact]
    public async Task OnDocumentSavedAsync_TriggersIndexerUpdate()
    {
        var integration = new LspIntegration(_indexer, _lspService);
        var filePath = "/src/Service.cs";

        await integration.OnDocumentSavedAsync(filePath, CancellationToken.None).ConfigureAwait(true);

        _mockIndexer.Verify(x => x.UpdateFileAsync(filePath, CancellationToken.None), Times.Once);

        integration.Dispose();
    }

    [Fact]
    public async Task OnWatchedFilesChangedAsync_TriggersIndexerUpdateForEachFile()
    {
        var integration = new LspIntegration(_indexer, _lspService);
        var filePaths = new[] { "/src/A.cs", "/src/B.cs", "/src/C.cs" };

        await integration.OnWatchedFilesChangedAsync(filePaths, CancellationToken.None).ConfigureAwait(true);

        _mockIndexer.Verify(x => x.UpdateFileAsync(It.IsAny<string>(), CancellationToken.None), Times.Exactly(3));

        integration.Dispose();
    }

    [Fact]
    public async Task OnDocumentChangedAsync_WhenIndexerThrows_DoesNotPropagate()
    {
        _mockIndexer.Setup(x => x.UpdateFileAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB error"));
        var integration = new LspIntegration(_indexer, _lspService);

        var exception = await Record.ExceptionAsync(() =>
            integration.OnDocumentChangedAsync("/src/Test.cs", CancellationToken.None)).ConfigureAwait(true);

        Assert.Null(exception);

        integration.Dispose();
    }

    [Fact]
    public async Task OnDocumentChangedAsync_WithNullFilePath_ThrowsArgumentNullException()
    {
        var integration = new LspIntegration(_indexer, _lspService);

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            integration.OnDocumentChangedAsync(null!, CancellationToken.None)).ConfigureAwait(true);

        integration.Dispose();
    }

    [Fact]
    public async Task OnDocumentChangedAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var integration = new LspIntegration(_indexer, _lspService);
        integration.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            integration.OnDocumentChangedAsync("/src/Test.cs", CancellationToken.None)).ConfigureAwait(true);
    }

    [Fact]
    public async Task TryFindDefinitionAsync_WithLspService_ReturnsLspResults()
    {
        var expected = new List<LspLocation>
        {
            new() { Uri = "file:///src/Service.cs", Range = new LspRange() }
        };
        _mockLspService.Setup(x => x.GotoDefinitionAsync("/src/Caller.cs", 10, 5, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var integration = new LspIntegration(_indexer, _lspService);
        var result = await integration.TryFindDefinitionAsync("/src/Caller.cs", 10, 5, CancellationToken.None).ConfigureAwait(true);

        Assert.Single(result);
        Assert.Equal("file:///src/Service.cs", result[0].Uri);

        integration.Dispose();
    }

    [Fact]
    public async Task TryFindDefinitionAsync_WithoutLspService_ReturnsEmpty()
    {
        var integration = new LspIntegration(_indexer);
        var result = await integration.TryFindDefinitionAsync("/src/Caller.cs", 10, 5, CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(result);

        integration.Dispose();
    }

    [Fact]
    public async Task TryFindDefinitionAsync_WhenLspThrows_ReturnsEmpty()
    {
        _mockLspService.Setup(x => x.GotoDefinitionAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LSP crashed"));

        var integration = new LspIntegration(_indexer, _lspService);
        var result = await integration.TryFindDefinitionAsync("/src/Caller.cs", 10, 5, CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(result);

        integration.Dispose();
    }

    [Fact]
    public async Task TryFindReferencesAsync_WithLspService_ReturnsLspResults()
    {
        var expected = new List<LspLocation>
        {
            new() { Uri = "file:///src/A.cs", Range = new LspRange() },
            new() { Uri = "file:///src/B.cs", Range = new LspRange() }
        };
        _mockLspService.Setup(x => x.FindReferencesAsync("/src/Service.cs", 5, 10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        var integration = new LspIntegration(_indexer, _lspService);
        var result = await integration.TryFindReferencesAsync("/src/Service.cs", 5, 10, CancellationToken.None).ConfigureAwait(true);

        Assert.Equal(2, result.Count);

        integration.Dispose();
    }

    [Fact]
    public async Task TryFindReferencesAsync_WithoutLspService_ReturnsEmpty()
    {
        var integration = new LspIntegration(_indexer);
        var result = await integration.TryFindReferencesAsync("/src/Service.cs", 5, 10, CancellationToken.None).ConfigureAwait(true);

        Assert.Empty(result);

        integration.Dispose();
    }

    [Fact]
    public async Task TryFindDefinitionAsync_AfterDispose_ThrowsObjectDisposedException()
    {
        var integration = new LspIntegration(_indexer, _lspService);
        integration.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() =>
            integration.TryFindDefinitionAsync("/src/Test.cs", 1, 1, CancellationToken.None)).ConfigureAwait(true);
    }
}
