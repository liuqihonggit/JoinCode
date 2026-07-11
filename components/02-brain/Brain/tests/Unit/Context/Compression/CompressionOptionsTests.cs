
namespace Core.Tests.Context.Compression;

public class CompressionOptionsTests
{
    [Fact]
    public void DefaultOptions_ShouldHaveCorrectValues()
    {
        var options = CompressionOptions.Default;

        options.TargetCompressionRatio.Should().Be(0.5);
        options.PreserveSignatures.Should().BeTrue();
        options.PreserveComments.Should().BeTrue();
        options.MaxOutputTokens.Should().Be(4000);
        options.PreserveImports.Should().BeTrue();
        options.PreserveTypeDefinitions.Should().BeTrue();
        options.DialogueRoundsToPreserve.Should().Be(3);
        options.UseSummarization.Should().BeTrue();
        options.MaxSummaryLength.Should().Be(500);
        options.PreserveKeyDecisions.Should().BeTrue();
        options.MaxReferenceEntries.Should().Be(100);
        options.EnableSmartCompression.Should().BeTrue();
        options.MinCompressionThreshold.Should().Be(100);
        options.CompressionTimeoutMs.Should().Be(5000);
        options.PreserveDocumentation.Should().BeTrue();
        options.MaxMethodBodyLines.Should().Be(0);
        options.PreserveConstants.Should().BeTrue();
        options.PreserveEnums.Should().BeTrue();
    }

    [Fact]
    public void LightOptions_ShouldHaveCorrectValues()
    {
        var options = CompressionOptions.Light;

        options.TargetCompressionRatio.Should().Be(0.8);
        options.PreserveSignatures.Should().BeTrue();
        options.PreserveComments.Should().BeTrue();
        options.MaxOutputTokens.Should().Be(8000);
        options.UseSummarization.Should().BeFalse();
    }

    [Fact]
    public void AggressiveOptions_ShouldHaveCorrectValues()
    {
        var options = CompressionOptions.Aggressive;

        options.TargetCompressionRatio.Should().Be(0.3);
        options.PreserveSignatures.Should().BeTrue();
        options.PreserveComments.Should().BeFalse();
        options.MaxOutputTokens.Should().Be(2000);
        options.UseSummarization.Should().BeTrue();
        options.MaxSummaryLength.Should().Be(200);
        options.PreserveImports.Should().BeFalse();
        options.PreserveDocumentation.Should().BeFalse();
    }

    [Fact]
    public void ForCodeOptions_ShouldHaveCorrectValues()
    {
        var options = CompressionOptions.ForCode;

        options.TargetCompressionRatio.Should().Be(0.4);
        options.PreserveSignatures.Should().BeTrue();
        options.PreserveComments.Should().BeTrue();
        options.PreserveImports.Should().BeTrue();
        options.PreserveTypeDefinitions.Should().BeTrue();
        options.PreserveDocumentation.Should().BeTrue();
        options.PreserveConstants.Should().BeTrue();
        options.PreserveEnums.Should().BeTrue();
        options.MaxMethodBodyLines.Should().Be(0);
        options.UseSummarization.Should().BeFalse();
    }

    [Fact]
    public void ForDialogueOptions_ShouldHaveCorrectValues()
    {
        var options = CompressionOptions.ForDialogue;

        options.TargetCompressionRatio.Should().Be(0.5);
        options.DialogueRoundsToPreserve.Should().Be(2);
        options.UseSummarization.Should().BeTrue();
        options.MaxSummaryLength.Should().Be(400);
        options.PreserveKeyDecisions.Should().BeTrue();
    }

    [Fact]
    public void ForReferenceIndexOptions_ShouldHaveCorrectValues()
    {
        var options = CompressionOptions.ForReferenceIndex;

        options.TargetCompressionRatio.Should().Be(0.6);
        options.MaxReferenceEntries.Should().Be(50);
        options.PreserveSignatures.Should().BeTrue();
        options.UseSummarization.Should().BeFalse();
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void TargetCompressionRatio_ShouldAcceptValidValues(double ratio)
    {
        var options = new CompressionOptions { TargetCompressionRatio = ratio };
        options.TargetCompressionRatio.Should().Be(ratio);
    }

    [Theory]
    [InlineData(-0.1)]
    [InlineData(1.1)]
    public void TargetCompressionRatio_InvalidValues_ShouldStillBeSettable(double ratio)
    {
        var options = new CompressionOptions { TargetCompressionRatio = ratio };
        options.TargetCompressionRatio.Should().Be(ratio);
    }

    [Fact]
    public void Options_ShouldBeIndependentlyModifiable()
    {
        var options1 = CompressionOptions.Default;
        var options2 = new CompressionOptions
        {
            TargetCompressionRatio = 0.3,
            PreserveComments = false
        };

        options1.TargetCompressionRatio.Should().Be(0.5);
        options1.PreserveComments.Should().BeTrue();
        options2.TargetCompressionRatio.Should().Be(0.3);
        options2.PreserveComments.Should().BeFalse();
    }
}
