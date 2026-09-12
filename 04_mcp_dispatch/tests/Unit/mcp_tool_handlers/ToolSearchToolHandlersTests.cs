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
    public async Task SearchToolsAsync_McpTool_IsDiscoverable()
    {
        var mcpHandler = new Mock<IToolHandler>();
        mcpHandler.SetupGet(x => x.Description).Returns("Remote MCP echo tool");
        mcpHandler.SetupGet(x => x.Kind).Returns(ToolKind.Mcp);
        var dict = new Dictionary<string, IToolHandler> { { "mcp__remote_echo", mcpHandler.Object } };
        _toolRegistry.Setup(x => x.GetAllToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(dict);

        var result = await _handler.SearchToolsAsync("echo", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.IsError);
        Assert.Contains("mcp__remote_echo", result.GetTextContent());
        Assert.Contains("Remote MCP echo tool", result.GetTextContent());
    }

    [Fact]
    public async Task SearchToolsAsync_McpToolExactMatch_RanksFirst()
    {
        var mcpHandler = new Mock<IToolHandler>();
        mcpHandler.SetupGet(x => x.Description).Returns("Remote MCP read tool");
        mcpHandler.SetupGet(x => x.Kind).Returns(ToolKind.Mcp);
        var systemHandler = new Mock<IToolHandler>();
        systemHandler.SetupGet(x => x.Description).Returns("Local file read");
        systemHandler.SetupGet(x => x.Kind).Returns(ToolKind.System);
        var dict = new Dictionary<string, IToolHandler>
        {
            { "file_read", systemHandler.Object },
            { "mcp__db_read", mcpHandler.Object },
        };
        _toolRegistry.Setup(x => x.GetAllToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(dict);

        var result = await _handler.SearchToolsAsync("read", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.IsError);
        var text = result.GetTextContent();
        var mcpIndex = text.IndexOf("mcp__db_read", StringComparison.Ordinal);
        var sysIndex = text.IndexOf("file_read", StringComparison.Ordinal);
        Assert.True(mcpIndex >= 0, "MCP tool should be found");
        Assert.True(sysIndex >= 0, "System tool should be found");
        Assert.True(mcpIndex < sysIndex, $"MCP tool 'mcp__db_read' (index {mcpIndex}) should rank before System tool 'file_read' (index {sysIndex})");
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
