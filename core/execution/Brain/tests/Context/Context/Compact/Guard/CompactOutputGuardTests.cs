namespace Brain.Tests.Context.Compact.Guard;

[Trait("Category", "Unit")]
public class CompactOutputGuardTests
{
    private readonly CompactOutputGuard _guard = new(NullLogger<CompactOutputGuard>.Instance);

    [Fact]
    public void Validate_NormalSummary_ReturnsValid()
    {
        var summary = "The user asked about implementing a new feature.\n" +
                      "The assistant provided a detailed response with code examples.\n" +
                      "They discussed multiple approaches and chose the simplest one.";
        var result = _guard.Validate(summary, originalMessageChars: 5000);

        result.IsValid.Should().BeTrue();
        result.FailureReason.Should().Be(CompactGuardFailureReason.None);
        result.FallbackLevel.Should().Be(CompactFallbackLevel.None);
    }

    [Fact]
    public void Validate_GibberishSummary_ReturnsInvalidWithMicrocompactFallback()
    {
        var random = new Random(42);
        var chars = new char[500];
        for (var i = 0; i < chars.Length; i++)
            chars[i] = (char)random.Next(33, 127);
        var gibberish = new string(chars);

        var result = _guard.Validate(gibberish, originalMessageChars: 5000);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(CompactGuardFailureReason.GibberishDetected);
        result.FallbackLevel.Should().Be(CompactFallbackLevel.Microcompact);
    }

    [Fact]
    public void Validate_CollapsedSummary_ReturnsInvalidWithTruncateFallback()
    {
        var result = _guard.Validate("ok", originalMessageChars: 5000);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(CompactGuardFailureReason.SummaryCollapsed);
        result.FallbackLevel.Should().Be(CompactFallbackLevel.Truncate);
    }

    [Fact]
    public void Validate_RepetitiveSummary_ReturnsSanitizeFallback()
    {
        var paragraph = "The user asked about implementing a new feature.";
        var repeated = string.Join("\n", Enumerable.Repeat(paragraph, 10));

        var result = _guard.Validate(repeated, originalMessageChars: 5000);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(CompactGuardFailureReason.RepetitionDetected);
        result.FallbackLevel.Should().Be(CompactFallbackLevel.Sanitize);
        result.SanitizedSummary.Should().NotBeEmpty();
    }

    [Fact]
    public void Validate_InterventionContamination_ReturnsSanitizeFallback()
    {
        var summary = "请用序号箭头方式总结当前回答再继续推理\n" +
                      "Some actual summary content here that is long enough for the collapse check. " +
                      "The user asked about implementing a new feature and the assistant provided " +
                      "a detailed response with code examples and discussed multiple approaches.";

        var result = _guard.Validate(summary, originalMessageChars: 5000);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(CompactGuardFailureReason.InterventionContamination);
        result.FallbackLevel.Should().Be(CompactFallbackLevel.Sanitize);
    }

    [Fact]
    public void Validate_EmptySummary_ReturnsTruncateFallback()
    {
        var result = _guard.Validate("", originalMessageChars: 5000);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be(CompactGuardFailureReason.SummaryCollapsed);
        result.FallbackLevel.Should().Be(CompactFallbackLevel.Truncate);
    }
}
