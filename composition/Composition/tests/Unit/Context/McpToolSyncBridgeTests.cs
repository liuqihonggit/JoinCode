namespace Core.Tests.Context;

public sealed partial class McpToolSyncBridgeTests
{
    private readonly Mock<IToolRegistry> _toolRegistry;
    private readonly Mock<IChatContextManager> _contextManager;
    [Inject] private readonly ILogger<McpToolSyncBridge> _logger;

    public McpToolSyncBridgeTests()
    {
        _toolRegistry = new Mock<IToolRegistry>();
        _contextManager = new Mock<IChatContextManager>();
        _logger = NullLogger<McpToolSyncBridge>.Instance;
    }

    private McpToolSyncBridge CreateSut() =>
        new(_toolRegistry.Object, _contextManager.Object, _logger);

    [Fact]
    public async Task OnToolsListChangedAsync_UpdatesToolSpecs()
    {
        var toolInfos = new List<ToolInfo>
        {
            new() { Name = "read_file", Description = "Read a file" },
            new() { Name = "write_file", Description = "Write a file" }
        };

        _toolRegistry.Setup(r => r.GetAllToolInfosAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(toolInfos.AsReadOnly());

        _contextManager.Setup(m => m.UpdateToolSpecsAsync(It.IsAny<IReadOnlyList<ToolSpec>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut();
        await sut.OnToolsListChangedAsync().ConfigureAwait(true);

        _contextManager.Verify(m => m.UpdateToolSpecsAsync(
            It.Is<IReadOnlyList<ToolSpec>>(specs => specs.Count == 2
                && specs[0].Name == "read_file"
                && specs[1].Name == "write_file"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnToolsListChangedAsync_EmptyTools_StillUpdates()
    {
        _toolRegistry.Setup(r => r.GetAllToolInfosAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);

        _contextManager.Setup(m => m.UpdateToolSpecsAsync(It.IsAny<IReadOnlyList<ToolSpec>>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var sut = CreateSut();
        await sut.OnToolsListChangedAsync().ConfigureAwait(true);

        _contextManager.Verify(m => m.UpdateToolSpecsAsync(
            It.Is<IReadOnlyList<ToolSpec>>(specs => specs.Count == 0),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task OnToolsListChangedAsync_RegistryException_DoesNotThrow()
    {
        _toolRegistry.Setup(r => r.GetAllToolInfosAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("registry error"));

        var sut = CreateSut();
        var act = async () => await sut.OnToolsListChangedAsync().ConfigureAwait(true);
        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task OnToolsListChangedAsync_ContextManagerException_DoesNotThrow()
    {
        _toolRegistry.Setup(r => r.GetAllToolInfosAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync([new ToolInfo { Name = "tool_a", Description = "desc" }]);

        _contextManager.Setup(m => m.UpdateToolSpecsAsync(It.IsAny<IReadOnlyList<ToolSpec>>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("context error"));

        var sut = CreateSut();
        var act = async () => await sut.OnToolsListChangedAsync().ConfigureAwait(true);
        await act.Should().NotThrowAsync().ConfigureAwait(true);
    }

    [Fact]
    public async Task OnToolsListChangedAsync_TransformsToolInfoToToolSpec()
    {
        var toolInfos = new List<ToolInfo>
        {
            new() { Name = "search", Description = "Search code" }
        };

        _toolRegistry.Setup(r => r.GetAllToolInfosAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(toolInfos.AsReadOnly());

        IReadOnlyList<ToolSpec>? capturedSpecs = null;
        _contextManager.Setup(m => m.UpdateToolSpecsAsync(It.IsAny<IReadOnlyList<ToolSpec>>(), It.IsAny<CancellationToken>()))
            .Callback<IReadOnlyList<ToolSpec>, CancellationToken>((specs, _) => capturedSpecs = specs)
            .Returns(Task.CompletedTask);

        var sut = CreateSut();
        await sut.OnToolsListChangedAsync().ConfigureAwait(true);

        capturedSpecs.Should().NotBeNull();
        capturedSpecs![0].Name.Should().Be("search");
        capturedSpecs[0].Description.Should().Be("Search code");
    }
}
