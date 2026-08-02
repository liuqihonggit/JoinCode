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

    [Fact]
    public void Detect_ExactlyThreeParagraphs_ReturnsClean()
    {
        var summary = "First unique paragraph.\n" +
                      "Second unique paragraph.\n" +
                      "Third unique paragraph.";

        var result = SummaryRepetitionDetector.Detect(summary);

        result.IsRepetition.Should().BeFalse();
    }

    [Fact]
    public void Detect_ThreeParagraphsWithTwoSimilar_ReturnsRepetition()
    {
        var summary = "The system is running normally.\n" +
                      "The system is running normally.\n" +
                      "A completely different conclusion here.";

        var result = SummaryRepetitionDetector.Detect(summary);

        result.IsRepetition.Should().BeTrue();
    }

    [Fact]
    public void Detect_ExactlyAtSimilarityThreshold_CountsAsDuplicate()
    {
        var summary = "a b c d\n" +
                      "a b c d e\n" +
                      "x y z w\n" +
                      "x y z w v";

        var result = SummaryRepetitionDetector.Detect(summary, new SummaryRepetitionOptions { SimilarityThreshold = 0.8, RepetitionRatioThreshold = 0.4, WindowSize = 3 });

        result.IsRepetition.Should().BeTrue();
    }

    [Fact]
    public void Detect_ExactlyAtRepetitionRatioThreshold_ReturnsClean()
    {
        var summary = "duplicate paragraph\n" +
                      "duplicate paragraph\n" +
                      "unique paragraph one\n" +
                      "unique paragraph two\n" +
                      "unique paragraph three";

        var result = SummaryRepetitionDetector.Detect(summary);

        result.IsRepetition.Should().BeFalse();
        result.RepetitionRatio.Should().Be(0.4);
    }

    [Fact]
    public void Detect_WithCustomOptions_UsesProvidedValues()
    {
        var summary = "a b c d\n" +
                      "a b c d e\n" +
                      "x y z w\n" +
                      "x y z w v";

        var result = SummaryRepetitionDetector.Detect(summary, new SummaryRepetitionOptions { SimilarityThreshold = 0.8, RepetitionRatioThreshold = 0.3, WindowSize = 3 });

        result.IsRepetition.Should().BeTrue();
    }
}
