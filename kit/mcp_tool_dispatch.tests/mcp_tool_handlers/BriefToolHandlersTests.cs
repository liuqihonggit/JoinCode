namespace Sync.Tests.ToolHandlers;

public class BriefToolHandlersTests
{
    private readonly Mock<IBriefModeService> _briefModeService = new();
    private readonly Mock<IBriefService> _briefService = new();
    private readonly BriefToolHandlers _handler;

    public BriefToolHandlersTests()
    {
        _briefService.Setup(x => x.FormatMessageWithPaths(
                It.IsAny<string>(),
                It.IsAny<string[]?>(),
                It.IsAny<bool>()))
            .Returns((string msg, string[]? att, bool pro) => $"[{(pro ? "proactive" : "normal")}] {msg}");

        _briefModeService.Setup(x => x.IsEnabled).Returns(false);
        _briefModeService.Setup(x => x.GetStatus()).Returns(BriefModeStatus.Disabled());

        _handler = new BriefToolHandlers(_briefModeService.Object, _briefService.Object);
    }

    [Fact]
    public async Task SendUserMessageAsync_SimpleMessage_ReturnsDelivered()
    {
        var result = await _handler.SendUserMessageAsync("Task completed").ConfigureAwait(true);

        Assert.False(result.IsError);
        Assert.Contains("Message delivered to user", result.GetTextContent());
    }

    [Fact]
    public async Task SendUserMessageAsync_WithAttachments_ReturnsWithAttachmentCount()
    {
        var result = await _handler.SendUserMessageAsync("Fix done", attachments: new[] { "changes.diff" }).ConfigureAwait(true);

        Assert.False(result.IsError);
        Assert.Contains("1 attachment included", result.GetTextContent());
    }

    [Fact]
    public async Task SendUserMessageAsync_MultipleAttachments_ReturnsPlural()
    {
        var result = await _handler.SendUserMessageAsync("Fix done", attachments: new[] { "a.diff", "b.diff" }).ConfigureAwait(true);

        Assert.False(result.IsError);
        Assert.Contains("2 attachments included", result.GetTextContent());
    }

    [Fact]
    public async Task SendUserMessageAsync_ProactiveStatus_PassesFlag()
    {
        var result = await _handler.SendUserMessageAsync("Background task done", status: "proactive").ConfigureAwait(true);

        Assert.False(result.IsError);
    }

    [Fact]
    public async Task SendUserMessageAsync_NoBriefService_ReturnsError()
    {
        var handler = new BriefToolHandlers(_briefModeService.Object, briefService: null);

        var result = await handler.SendUserMessageAsync("test").ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("not initialized", result.GetTextContent());
    }

    [Fact]
    public async Task SendUserMessageAsync_EmptyMessage_ReturnsError()
    {
        var result = await _handler.SendUserMessageAsync("").ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("cannot be empty", result.GetTextContent());
    }

    [Fact]
    public async Task SendUserMessageAsync_EmptyAttachmentPath_ReturnsError()
    {
        var result = await _handler.SendUserMessageAsync("msg", attachments: new[] { "" }).ConfigureAwait(true);

        Assert.True(result.IsError);
        Assert.Contains("cannot be empty", result.GetTextContent());
    }
}
