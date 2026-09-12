namespace Core.Agents;


public sealed class SubAgentOutputTruncatorTests
{
    private const string ArchiveDir = "X:\\tmp\\subagent";

    private static SubAgentOutputTruncator CreateSut(Mock<IFileSystem> fsMock)
    {
        return new SubAgentOutputTruncator(fsMock.Object, NullLogger<SubAgentOutputTruncator>.Instance, ArchiveDir);
    }

    [Fact]
    public void EstimateTokens_ReturnsLengthDividedByFour()
    {
        SubAgentOutputTruncator.EstimateTokens(new string('a', 400)).Should().Be(100);
        SubAgentOutputTruncator.EstimateTokens(new string('a', 399)).Should().Be(99);
    }

    [Fact]
    public async Task TruncateAsync_WithinBudget_ReturnsOriginal_NoArchive()
    {
        var fsMock = new Mock<IFileSystem>();
        var sut = CreateSut(fsMock);

        var result = await sut.TruncateAsync("agent-1", "small output", 100);

        result.WasTruncated.Should().BeFalse();
        result.ArchivedPath.Should().BeNull();
        result.FinalText.Should().Be("small output");
        fsMock.Verify(x => x.WriteAllTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task TruncateAsync_ExceedsBudget_ArchivesAndReturnsPointer()
    {
        var fsMock = new Mock<IFileSystem>();
        fsMock.Setup(x => x.DirectoryExists(It.IsAny<string>())).Returns(false);
        string? capturedContent = null;
        string? capturedPath = null;
        fsMock.Setup(x => x.WriteAllTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .Callback<string, string, CancellationToken>((p, c, _) => { capturedPath = p; capturedContent = c; })
              .Returns(Task.CompletedTask);
        var sut = CreateSut(fsMock);

        var bigOutput = new string('x', 1000);
        var result = await sut.TruncateAsync("agent-2", bigOutput, 10);

        result.WasTruncated.Should().BeTrue();
        result.ArchivedPath.Should().NotBeNull();
        capturedContent.Should().Be(bigOutput);
        capturedPath.Should().NotBeNull();
        result.FinalText.Should().Contain("agent-2");
        result.FinalText.Should().Contain("read 查看");
        result.FinalText.Should().NotContain(bigOutput);
    }

    [Fact]
    public async Task TruncateAsync_WithSummary_PointerContainsSummary()
    {
        var fsMock = new Mock<IFileSystem>();
        fsMock.Setup(x => x.DirectoryExists(It.IsAny<string>())).Returns(true);
        fsMock.Setup(x => x.WriteAllTextAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
              .Returns(Task.CompletedTask);
        var sut = CreateSut(fsMock);

        var result = await sut.TruncateAsync("agent-3", new string('y', 800), 10, "修复了登录bug");

        result.FinalText.Should().Contain("概要：修复了登录bug");
    }

    [Fact]
    public async Task TruncateAsync_EmptyOutput_ReturnsAsIs()
    {
        var fsMock = new Mock<IFileSystem>();
        var sut = CreateSut(fsMock);

        var result = await sut.TruncateAsync("agent-4", "", 10);

        result.WasTruncated.Should().BeFalse();
        result.FinalText.Should().Be("");
    }

    [Fact]
    public async Task TruncateAsync_ExactBudgetBoundary_ReturnsOriginal()
    {
        var fsMock = new Mock<IFileSystem>();
        var sut = CreateSut(fsMock);

        var output = new string('z', 400);
        var result = await sut.TruncateAsync("agent-5", output, 100);

        result.WasTruncated.Should().BeFalse();
        result.FinalText.Should().Be(output);
    }
}
