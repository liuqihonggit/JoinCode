namespace Sync.Tests.ToolHandlers;

public class CtxInspectToolHandlersTests
{
    private readonly Mock<IChatContextManager> _contextManager = new();
    private readonly CtxInspectToolHandlers _handler;

    public CtxInspectToolHandlersTests()
    {
        _contextManager.Setup(x => x.GetContextMaxTokens()).Returns(128000);
        _contextManager.Setup(x => x.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MessageList());
        _contextManager.Setup(x => x.GetDeferredTools())
            .Returns(new List<DeferredToolInfo>().AsReadOnly());

        _handler = new CtxInspectToolHandlers(_contextManager.Object, NullLogger<CtxInspectToolHandlers>.Instance);
    }

    [Fact]
    public async Task InspectContextAsync_Summary_ReturnsSuccess()
    {
        var result = await _handler.InspectContextAsync("summary", CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.IsError);
        Assert.Contains("上下文检查", result.GetTextContent());
        Assert.Contains("128000", result.GetTextContent());
    }

    [Fact]
    public async Task InspectContextAsync_Detailed_ReturnsSuccess()
    {
        var result = await _handler.InspectContextAsync("detailed", CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.IsError);
        Assert.Contains("上下文检查", result.GetTextContent());
    }

    [Fact]
    public async Task InspectContextAsync_Exception_ReturnsError()
    {
        _contextManager.Setup(x => x.GetMessageListAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await _handler.InspectContextAsync("summary", CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("上下文检查失败", result.GetTextContent());
    }
}
