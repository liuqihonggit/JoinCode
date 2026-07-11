namespace Sync.Tests.ToolHandlers;

public class ToolSearchToolHandlersTests
{
    private readonly Mock<IMcpToolRegistry> _toolRegistry = new();
    private readonly ToolSearchToolHandlers _handler;

    public ToolSearchToolHandlersTests()
    {
        _handler = new ToolSearchToolHandlers(_toolRegistry.Object, NullLogger<ToolSearchToolHandlers>.Instance);
    }

    [Fact]
    public async Task SearchToolsAsync_EmptyQuery_ReturnsError()
    {
        var result = await _handler.SearchToolsAsync("", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("不能为空", result.GetTextContent());
    }

    [Fact]
    public async Task SearchToolsAsync_NoTools_ReturnsSuccess()
    {
        _toolRegistry.Setup(x => x.GetAllToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IToolHandler>());

        var result = await _handler.SearchToolsAsync("test", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.IsError);
        Assert.Contains("未找到匹配的工具", result.GetTextContent());
    }

    [Fact]
    public async Task SearchToolsAsync_WithTools_ReturnsSuccess()
    {
        var mockHandler = new Mock<IToolHandler>();
        mockHandler.SetupGet(x => x.Description).Returns("A test tool");
        var dict = new Dictionary<string, IToolHandler> { { "test_tool", mockHandler.Object } };
        _toolRegistry.Setup(x => x.GetAllToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(dict);

        var result = await _handler.SearchToolsAsync("test", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.IsError);
        Assert.Contains("test_tool", result.GetTextContent());
    }

    [Fact]
    public async Task SearchToolsAsync_RegistryThrows_ReturnsError()
    {
        _toolRegistry.Setup(x => x.GetAllToolsAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await _handler.SearchToolsAsync("test", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("工具搜索失败", result.GetTextContent());
    }
}
