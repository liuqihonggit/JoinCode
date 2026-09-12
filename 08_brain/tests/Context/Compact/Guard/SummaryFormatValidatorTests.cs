namespace Brain.Tests.Context.Compact.Guard;

[Trait("Category", "Unit")]
public class SummaryFormatValidatorTests
{
    [Fact]
    public void Validate_InterventionKeyword_ReturnsContamination()
    {
        var summary = "请用序号箭头方式总结当前回答再继续推理\nSome actual summary content here that is long enough.";
        var result = SummaryFormatValidator.Validate(summary);

        result.HasInterventionContamination.Should().BeTrue();
    }

    [Fact]
    public void Validate_SelfReference_ReturnsContamination()
    {
        var summary = "我会继续分析这个问题并提供更多细节。\nThe user asked about features.";
        var result = SummaryFormatValidator.Validate(summary);

        result.HasSelfReference.Should().BeTrue();
    }

    [Fact]
    public void Validate_NormalSummary_ReturnsClean()
    {
        var summary = "The user asked about implementing a new feature.\n" +
                      "The assistant provided a detailed response with code examples.";
        var result = SummaryFormatValidator.Validate(summary);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_UnclosedSummaryTag_ReturnsFormatError()
    {
        var summary = "<summary>This is the content without closing tag";
        var result = SummaryFormatValidator.Validate(summary);

        result.HasFormatError.Should().BeTrue();
    }

    [Fact]
    public void Validate_ClosedSummaryTag_ReturnsClean()
    {
        var summary = "<summary>This is the content</summary>";
        var result = SummaryFormatValidator.Validate(summary);

        result.HasFormatError.Should().BeFalse();
    }

    [Fact]
    public void Validate_TruncationMarker_ReturnsFormatError()
    {
        var summary = "This is a summary that was [被截断] due to length limits.";
        var result = SummaryFormatValidator.Validate(summary);

        result.HasTruncationMarker.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyString_ReturnsFormatError()
    {
        var result = SummaryFormatValidator.Validate("");

        result.IsValid.Should().BeFalse();
    }
}
