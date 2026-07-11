namespace Core.Context;

public sealed class CompactSummaryMarkerTests
{
    private readonly Mock<IStateService> _stateService = new();

    private ChatContextManager CreateSut()
    {
        return new ChatContextManager(_stateService.Object, NullLogger<ChatContextManager>.Instance);
    }

    [Fact]
    public async Task AddCompactSummaryMessageAsync_CreatesMessageWithMarker()
    {
        var sut = CreateSut();
        await sut.AddCompactSummaryMessageAsync("summary content").ConfigureAwait(true);

        var messages = await sut.GetMessageListAsync().ConfigureAwait(true);
        messages.Should().HaveCountGreaterThanOrEqualTo(1);

        var summaryMsg = messages.FirstOrDefault(m =>
            m.Metadata != null && m.Metadata.ContainsKey("isCompactSummary"));
        summaryMsg.Should().NotBeNull();
        summaryMsg!.Role.Should().Be(MessageRole.User);
        summaryMsg.Metadata!["isCompactSummary"].GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task AddCompactSummaryMessageAsync_ContentPreserved()
    {
        var sut = CreateSut();
        await sut.AddCompactSummaryMessageAsync("[上下文压缩摘要]\nUser asked about X").ConfigureAwait(true);

        var messages = await sut.GetMessageListAsync().ConfigureAwait(true);
        var summaryMsg = messages.FirstOrDefault(m =>
            m.Metadata != null && m.Metadata.ContainsKey("isCompactSummary"));
        summaryMsg.Should().NotBeNull();
        summaryMsg!.Content.Should().Contain("[上下文压缩摘要]");
    }

    [Fact]
    public async Task AddUserMessageAsync_NoMarker()
    {
        var sut = CreateSut();
        await sut.AddUserMessageAsync("normal user message").ConfigureAwait(true);

        var messages = await sut.GetMessageListAsync().ConfigureAwait(true);
        var normalMsg = messages.FirstOrDefault(m => m.Content == "normal user message");
        normalMsg.Should().NotBeNull();
        normalMsg!.Metadata.Should().BeNull("normal user messages should not have isCompactSummary marker");
    }
}
