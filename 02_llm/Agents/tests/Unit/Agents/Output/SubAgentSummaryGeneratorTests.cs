namespace Core.Agents;


public sealed class SubAgentSummaryGeneratorTests
{
    private static string GenerateText(int tokens) => new('x', tokens * 4);

    [Fact]
    public async Task ClientNull_ReturnsSkipped()
    {
        var sut = new SubAgentSummaryGenerator(client: null, config: new SubAgentSummaryConfig { Auto = true });

        var result = await sut.TrySummarizeAsync("agent-1", GenerateText(100), 50);

        result.Status.Should().Be(SubAgentSummaryStatus.Skipped);
        result.Summary.Should().BeNull();
    }

    [Fact]
    public async Task AutoDisabled_ReturnsSkipped()
    {
        var clientMock = new Mock<ISubAgentSummaryClient>();
        var sut = new SubAgentSummaryGenerator(clientMock.Object, new SubAgentSummaryConfig { Auto = false });

        var result = await sut.TrySummarizeAsync("agent-1", GenerateText(100), 50);

        result.Status.Should().Be(SubAgentSummaryStatus.Skipped);
        clientMock.Verify(c => c.SummarizeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task OutputWithinBudget_ReturnsNotNeeded()
    {
        var clientMock = new Mock<ISubAgentSummaryClient>();
        var sut = new SubAgentSummaryGenerator(clientMock.Object, new SubAgentSummaryConfig { Auto = true });

        var result = await sut.TrySummarizeAsync("agent-1", GenerateText(50), 100);

        result.Status.Should().Be(SubAgentSummaryStatus.NotNeeded);
        clientMock.Verify(c => c.SummarizeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task LlmReturnsValidSummary_ReturnsSuccess()
    {
        var clientMock = new Mock<ISubAgentSummaryClient>();
        clientMock
            .Setup(c => c.SummarizeAsync(It.IsAny<string>(), "agent-1", 50, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenerateText(30));

        var sut = new SubAgentSummaryGenerator(clientMock.Object, new SubAgentSummaryConfig { Auto = true });

        var result = await sut.TrySummarizeAsync("agent-1", GenerateText(100), 50);

        result.Status.Should().Be(SubAgentSummaryStatus.Success);
        result.Summary.Should().NotBeNull();
        SubAgentOutputTruncator.EstimateTokens(result.Summary!).Should().Be(30);
    }

    [Fact]
    public async Task LlmReturnsNull_ReturnsFailed()
    {
        var clientMock = new Mock<ISubAgentSummaryClient>();
        clientMock
            .Setup(c => c.SummarizeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string?)null);

        var sut = new SubAgentSummaryGenerator(clientMock.Object, new SubAgentSummaryConfig { Auto = true, MaxRetries = 0 });

        var result = await sut.TrySummarizeAsync("agent-1", GenerateText(100), 50);

        result.Status.Should().Be(SubAgentSummaryStatus.Failed);
        result.Summary.Should().BeNull();
    }

    [Fact]
    public async Task LlmReturnsOversizedSummary_ReturnsFailed()
    {
        var clientMock = new Mock<ISubAgentSummaryClient>();
        clientMock
            .Setup(c => c.SummarizeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GenerateText(80));

        var sut = new SubAgentSummaryGenerator(clientMock.Object, new SubAgentSummaryConfig { Auto = true, MaxRetries = 0 });

        var result = await sut.TrySummarizeAsync("agent-1", GenerateText(100), 50);

        result.Status.Should().Be(SubAgentSummaryStatus.Failed);
    }

    [Fact]
    public async Task LlmThrowsThenSucceeds_ReturnsSuccess()
    {
        var clientMock = new Mock<ISubAgentSummaryClient>();
        clientMock
            .SetupSequence(c => c.SummarizeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("transient"))
            .ReturnsAsync(GenerateText(30));

        var sut = new SubAgentSummaryGenerator(clientMock.Object, new SubAgentSummaryConfig { Auto = true, MaxRetries = 1 });

        var result = await sut.TrySummarizeAsync("agent-1", GenerateText(100), 50);

        result.Status.Should().Be(SubAgentSummaryStatus.Success);
        result.Summary.Should().NotBeNull();
    }

    [Fact]
    public async Task LlmThrowsAllRetries_ReturnsFailed()
    {
        var clientMock = new Mock<ISubAgentSummaryClient>();
        clientMock
            .Setup(c => c.SummarizeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("always fails"));

        var sut = new SubAgentSummaryGenerator(clientMock.Object, new SubAgentSummaryConfig { Auto = true, MaxRetries = 2 });

        var result = await sut.TrySummarizeAsync("agent-1", GenerateText(100), 50);

        result.Status.Should().Be(SubAgentSummaryStatus.Failed);
        clientMock.Verify(c => c.SummarizeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }

    [Fact]
    public async Task CancellationToken_PropagatesToClient()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var clientMock = new Mock<ISubAgentSummaryClient>();
        clientMock
            .Setup(c => c.SummarizeAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>(), cts.Token))
            .ThrowsAsync(new OperationCanceledException(cts.Token));

        var sut = new SubAgentSummaryGenerator(clientMock.Object, new SubAgentSummaryConfig { Auto = true, MaxRetries = 1 });

        var act = async () => await sut.TrySummarizeAsync("agent-1", GenerateText(100), 50, cts.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
