namespace Core.Tests.Context.Collapse;

public class ContextCollapseServiceTests
{
    private readonly ContextCollapseService _service = new(NullLogger<ContextCollapseService>.Instance);

    [Fact]
    public async Task CollapseAsync_EmptyContent_ShouldThrowArgumentException()
    {
        var act = async () => await _service.CollapseAsync("").ConfigureAwait(true);
        await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task CollapseAsync_ShortContent_ShouldNotCollapse()
    {
        var result = await _service.CollapseAsync("short content").ConfigureAwait(true);

        result.Should().NotBeNull();
        result.Collapsed.Should().BeFalse();
        result.CollapsedContent.Should().Be("short content");
    }

    [Fact]
    public async Task CollapseAsync_LongContentWithCodeBlocks_ShouldIdentifySegments()
    {
        var content = BuildContentWithCodeBlocks(5);
        var segments = await _service.IdentifyCollapsibleSegmentsAsync(content).ConfigureAwait(true);

        segments.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CollapseAsync_WithAggressiveOptions_ShouldCollapseMore()
    {
        var content = BuildContentWithCodeBlocks(10);
        var options = ContextCollapseOptions.Aggressive;

        var result = await _service.CollapseAsync(content, options).ConfigureAwait(true);

        result.Should().NotBeNull();
        result.Strategy.Should().Be(CollapseStrategy.Aggressive);
    }

    [Fact]
    public async Task CollapseAsync_WithConservativeOptions_ShouldPreserveMore()
    {
        var content = BuildContentWithCodeBlocks(10);
        var options = ContextCollapseOptions.Conservative;

        var result = await _service.CollapseAsync(content, options).ConfigureAwait(true);

        result.Should().NotBeNull();
        result.Strategy.Should().Be(CollapseStrategy.Conservative);
    }

    [Fact]
    public async Task CollapseAsync_WithBalancedOptions_ShouldUseBalancedStrategy()
    {
        var content = BuildContentWithCodeBlocks(10);
        var options = ContextCollapseOptions.Balanced;

        var result = await _service.CollapseAsync(content, options).ConfigureAwait(true);

        result.Should().NotBeNull();
        result.Strategy.Should().Be(CollapseStrategy.Balanced);
    }

    [Fact]
    public async Task CollapseAsync_ResultShouldContainTokenCounts()
    {
        var content = BuildContentWithCodeBlocks(8);

        var result = await _service.CollapseAsync(content).ConfigureAwait(true);

        result.OriginalTokenCount.Should().BeGreaterThan(0);
        result.CollapsedTokenCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task IdentifyCollapsibleSegmentsAsync_EmptyContent_ShouldThrowArgumentException()
    {
        var act = async () => await _service.IdentifyCollapsibleSegmentsAsync("").ConfigureAwait(true);
        await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task IdentifyCollapsibleSegmentsAsync_ShortContent_ShouldReturnEmpty()
    {
        var segments = await _service.IdentifyCollapsibleSegmentsAsync("short content").ConfigureAwait(true);

        segments.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateSummaryAsync_ShouldReturnSummary()
    {
        var content = BuildContentWithCodeBlocks(5);
        var segments = await _service.IdentifyCollapsibleSegmentsAsync(content).ConfigureAwait(true);

        if (segments.Count > 0)
        {
            var summary = await _service.GenerateSummaryAsync(segments[0]).ConfigureAwait(true);
            summary.Should().NotBeNullOrEmpty();
        }
    }

    private static string BuildContentWithCodeBlocks(int blockCount)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < blockCount; i++)
        {
            sb.AppendLine($"Some text before code block {i}.");
            sb.AppendLine("```csharp");
            sb.AppendLine($"// Code block {i}");
            sb.AppendLine("var x = " + string.Join("+", Enumerable.Range(0, 50).Select(n => $"n{n}")) + ";");
            sb.AppendLine("```");
            sb.AppendLine($"Some text after code block {i}.");
        }
        return sb.ToString();
    }
}
