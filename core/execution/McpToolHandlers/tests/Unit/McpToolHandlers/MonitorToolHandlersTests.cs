namespace Sync.Tests.ToolHandlers;

public class MonitorToolHandlersTests
{
    private readonly Mock<IMcpToolRegistry> _toolRegistry = new();
    private readonly MonitorToolHandlers _handler;

    public MonitorToolHandlersTests()
    {
        _toolRegistry.Setup(x => x.GetLocalToolCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(5);
        _toolRegistry.Setup(x => x.GetRemoteClientCountAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(2);
        _toolRegistry.Setup(x => x.GetAllToolsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, IToolHandler>());

        _handler = new MonitorToolHandlers(_toolRegistry.Object, NullLogger<MonitorToolHandlers>.Instance);
    }

    [Fact]
    public async Task MonitorMcpAsync_Status_ReturnsSuccess()
    {
        var result = await _handler.MonitorMcpAsync("status", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.IsError);
        Assert.Contains("MCP 状态概览", result.GetTextContent());
    }

    [Fact]
    public async Task MonitorMcpAsync_Tools_ReturnsSuccess()
    {
        var result = await _handler.MonitorMcpAsync("tools", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.IsError);
        Assert.Contains("已注册工具", result.GetTextContent());
    }

    [Fact]
    public async Task MonitorMcpAsync_InvalidType_ReturnsError()
    {
        var result = await _handler.MonitorMcpAsync("invalid", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("未知监控类型", result.GetTextContent());
    }
}
