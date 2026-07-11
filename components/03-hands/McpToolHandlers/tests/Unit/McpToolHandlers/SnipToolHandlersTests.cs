namespace Sync.Tests.ToolHandlers;

public class SnipToolHandlersTests
{
    private readonly Mock<IChatContextManager> _contextManager = new();
    private readonly SnipToolHandlers _handler;

    public SnipToolHandlersTests()
    {
        _contextManager.Setup(x => x.RewindLastTurnAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(RewindResult.Ok(RewindKind.TrimLastTurn, 2, 5));
        _contextManager.Setup(x => x.RewindToMessageIndexAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(RewindResult.Ok(RewindKind.TruncateToIndex, 3, 4));
        _contextManager.Setup(x => x.RewindToStartAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(RewindResult.Ok(RewindKind.ClearHistory, 10, 0));

        _handler = new SnipToolHandlers(_contextManager.Object, NullLogger<SnipToolHandlers>.Instance);
    }

    [Fact]
    public async Task SnipHistoryAsync_Rewind_ReturnsSuccess()
    {
        var result = await _handler.SnipHistoryAsync("rewind", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.IsError);
        Assert.Contains("已撤回最后一轮对话", result.GetTextContent());
    }

    [Fact]
    public async Task SnipHistoryAsync_RewindToWithoutIndex_ReturnsError()
    {
        var result = await _handler.SnipHistoryAsync("rewind_to", message_index: null, cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("message_index", result.GetTextContent());
    }

    [Fact]
    public async Task SnipHistoryAsync_RewindToWithIndex_ReturnsSuccess()
    {
        var result = await _handler.SnipHistoryAsync("rewind_to", message_index: 3, cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.IsError);
        Assert.Contains("已撤回到消息索引", result.GetTextContent());
    }

    [Fact]
    public async Task SnipHistoryAsync_Clear_ReturnsSuccess()
    {
        var result = await _handler.SnipHistoryAsync("clear", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.False(result.IsError);
        Assert.Contains("已清空全部对话历史", result.GetTextContent());
    }

    [Fact]
    public async Task SnipHistoryAsync_InvalidMode_ReturnsError()
    {
        var result = await _handler.SnipHistoryAsync("invalid", cancellationToken: CancellationToken.None).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("未知的裁剪模式", result.GetTextContent());
    }
}
