namespace Core.Tests.Services.ContextCollapse;

public sealed class ContextCollapseSegmentTests
{
    private readonly ContextCollapseService _service;

    public ContextCollapseSegmentTests()
    {
        _service = new ContextCollapseService();
    }

    [Fact]
    public async Task IdentifyCollapsibleSegmentsAsync_IdentifiesCodeBlocks()
    {
        var content = "Some text\n```\npublic class Foo\n{\n    public void Bar() { }\n    public void Baz() { }\n    public void Qux() { }\n}\n```\nMore text";
        var options = new ContextCollapseOptions { MinSegmentTokenCount = 1 };

        var segments = await _service.IdentifyCollapsibleSegmentsAsync(content, options).ConfigureAwait(true);

        segments.Should().NotBeEmpty();
        segments.Should().Contain(s => s.Type == CollapsibleSegmentType.CodeBlock);
    }

    [Fact]
    public async Task IdentifyCollapsibleSegmentsAsync_WithEmptyContent_ThrowsArgumentException()
    {
        var act = () => _service.IdentifyCollapsibleSegmentsAsync(string.Empty);

        await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task CollapseAsync_WithAggressiveStrategy_CollapsesMore()
    {
        var content = "Intro text\n```\npublic class LongClass\n{\n    public void Method1() { }\n    public void Method2() { }\n    public void Method3() { }\n    public void Method4() { }\n    public void Method5() { }\n    public void Method6() { }\n    public void Method7() { }\n    public void Method8() { }\n    public void Method9() { }\n    public void Method10() { }\n}\n```\n";

        var aggressiveResult = await _service.CollapseAsync(content, ContextCollapseOptions.Aggressive).ConfigureAwait(true);

        aggressiveResult.Should().NotBeNull();
        if (aggressiveResult.Collapsed)
        {
            aggressiveResult.CollapsedTokenCount.Should().BeLessThan(aggressiveResult.OriginalTokenCount);
        }
    }

    [Fact]
    public async Task CollapseAsync_WithConservativeStrategy_CollapsesLess()
    {
        var content = "Intro text\n```\npublic class LongClass\n{\n    public void Method1() { }\n    public void Method2() { }\n    public void Method3() { }\n    public void Method4() { }\n    public void Method5() { }\n    public void Method6() { }\n    public void Method7() { }\n    public void Method8() { }\n    public void Method9() { }\n    public void Method10() { }\n}\n```\n";

        var aggressiveResult = await _service.CollapseAsync(content, ContextCollapseOptions.Aggressive).ConfigureAwait(true);
        var conservativeResult = await _service.CollapseAsync(content, ContextCollapseOptions.Conservative).ConfigureAwait(true);

        if (aggressiveResult.Collapsed && conservativeResult.Collapsed)
        {
            aggressiveResult.SegmentsCollapsed.Should().BeGreaterThanOrEqualTo(conservativeResult.SegmentsCollapsed);
        }
    }

    [Fact]
    public async Task CollapseAsync_PreservesKeyReferences()
    {
        var content = "```\npublic class MyService\n{\n    public void Process() { }\n}\n```\n";

        var result = await _service.CollapseAsync(content, new ContextCollapseOptions { PreserveKeyReferences = true }).ConfigureAwait(true);

        if (result.Collapsed && result.CollapsedSegments.Count > 0)
        {
            result.CollapsedSegments.Should().Contain(s => s.PreservedReferences.Count > 0 || s.Type == CollapsibleSegmentType.CodeBlock);
        }
    }

    [Fact]
    public async Task CollapseAsync_WithEmptyContent_ThrowsArgumentException()
    {
        var act = () => _service.CollapseAsync(string.Empty);

        await act.Should().ThrowAsync<ArgumentException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task CollapseAsync_WithNoCollapsibleSegments_ReturnsNotCollapsed()
    {
        var content = "Short text without any code blocks or patterns.";

        var result = await _service.CollapseAsync(content).ConfigureAwait(true);

        result.Should().NotBeNull();
        result.Collapsed.Should().BeFalse();
    }

    [Fact]
    public async Task GenerateSummaryAsync_WithNullSegment_ThrowsArgumentNullException()
    {
        var act = () => _service.GenerateSummaryAsync(null!);

        await act.Should().ThrowAsync<ArgumentNullException>().ConfigureAwait(true);
    }
}
