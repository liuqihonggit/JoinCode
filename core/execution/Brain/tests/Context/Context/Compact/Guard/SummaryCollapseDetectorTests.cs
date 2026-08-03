namespace Brain.Tests.Context.Compact.Guard;

[Trait("Category", "Unit")]
public class SummaryCollapseDetectorTests
{
    [Fact]
    public void Detect_TooShortSummary_ReturnsCollapsed()
    {
        var result = SummaryCollapseDetector.Detect("ok", originalMessageChars: 5000);

        result.IsCollapsed.Should().BeTrue();
    }

    [Fact]
    public void Detect_LowCompressionRatio_ReturnsCollapsed()
    {
        var summary = new string('x', 30);
        var result = SummaryCollapseDetector.Detect(summary, originalMessageChars: 5000);

        result.IsCollapsed.Should().BeTrue();
    }

    [Fact]
    public void Detect_NormalSummary_ReturnsClean()
    {
        var summary = "The user asked about implementing a new feature. The assistant provided " +
                      "a detailed response with code examples and discussed multiple approaches.";
        var result = SummaryCollapseDetector.Detect(summary, originalMessageChars: 5000);

        result.IsCollapsed.Should().BeFalse();
    }

    [Fact]
    public void Detect_ShortOriginal_SkipsRatioCheck()
    {
        var result = SummaryCollapseDetector.Detect("short", originalMessageChars: 100);

        result.IsCollapsed.Should().BeTrue();
    }

    [Fact]
    public void Detect_EmptySummary_ReturnsCollapsed()
    {
        var result = SummaryCollapseDetector.Detect("", originalMessageChars: 5000);

        result.IsCollapsed.Should().BeTrue();
    }

    [Fact]
    public void Detect_TemplateOnlySummary_ReturnsCollapsed()
    {
        var result = SummaryCollapseDetector.Detect("Summary:", originalMessageChars: 5000);

        result.IsCollapsed.Should().BeTrue();
    }
}
