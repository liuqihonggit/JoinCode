namespace Brain.Tests.Context.Compact.Guard;

[Trait("Category", "Unit")]
public class SummaryRepetitionDetectorTests
{
    [Fact]
    public void Detect_RepeatedParagraphs_ReturnsRepetition()
    {
        var paragraph = "The user asked about implementing a new feature.";
        var repeated = string.Join("\n", Enumerable.Repeat(paragraph, 10));
        var result = SummaryRepetitionDetector.Detect(repeated);

        result.IsRepetition.Should().BeTrue();
    }

    [Fact]
    public void Detect_NormalSummary_ReturnsClean()
    {
        var summary = "The user asked about implementing a new feature.\n" +
                      "The assistant provided a detailed response.\n" +
                      "They discussed multiple approaches and chose the simplest one.\n" +
                      "The code was written and tested successfully.";

        var result = SummaryRepetitionDetector.Detect(summary);

        result.IsRepetition.Should().BeFalse();
    }

    [Fact]
    public void Detect_SingleParagraph_ReturnsClean()
    {
        var result = SummaryRepetitionDetector.Detect("Just one paragraph here.");

        result.IsRepetition.Should().BeFalse();
    }

    [Fact]
    public void Detect_EmptyString_ReturnsClean()
    {
        var result = SummaryRepetitionDetector.Detect("");

        result.IsRepetition.Should().BeFalse();
    }

    [Fact]
    public void Detect_SimilarButNotIdentical_ReturnsClean()
    {
        var summary = "The user asked about feature A.\n" +
                      "The user asked about feature B.\n" +
                      "The user asked about feature C.\n" +
                      "The assistant responded to each question.";

        var result = SummaryRepetitionDetector.Detect(summary);

        result.IsRepetition.Should().BeFalse();
    }
}
