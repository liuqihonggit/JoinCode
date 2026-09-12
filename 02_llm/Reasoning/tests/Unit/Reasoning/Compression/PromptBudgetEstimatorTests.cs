namespace JoinCode.Reasoning.Tests.Compression;

public sealed class PromptBudgetEstimatorTests
{
    [Fact]
    public void Estimate_EmptyString_ReturnsZero()
    {
        PromptBudgetEstimator.Estimate(string.Empty).Should().Be(0);
    }

    [Fact]
    public void Estimate_NullString_ReturnsZero()
    {
        PromptBudgetEstimator.Estimate(null!).Should().Be(0);
    }

    [Fact]
    public void Estimate_EnglishText_ReturnsReasonableEstimate()
    {
        var text = "Hello world this is a test";
        var result = PromptBudgetEstimator.Estimate(text);

        result.Should().BeGreaterThan(0);
        result.Should().BeLessThan(text.Length);
    }

    [Fact]
    public void Estimate_ChineseText_ReturnsReasonableEstimate()
    {
        var text = "这是一个中文测试文本";
        var result = PromptBudgetEstimator.Estimate(text);

        result.Should().BeGreaterThan(0);
        result.Should().BeLessThan(text.Length * 2);
    }

    [Fact]
    public void Estimate_MixedText_ChineseCostsMoreThanEnglish()
    {
        var chineseText = new string('测', 100);
        var englishText = new string('a', 100);

        var chineseTokens = PromptBudgetEstimator.Estimate(chineseText);
        var englishTokens = PromptBudgetEstimator.Estimate(englishText);

        chineseTokens.Should().BeGreaterThan(englishTokens,
            "because CJK characters consume more tokens than Latin characters");
    }

    [Fact]
    public void Estimate_LongText_ReturnsProportionalEstimate()
    {
        var shortText = "Hello";
        var longText = new string('a', 10000);

        var shortResult = PromptBudgetEstimator.Estimate(shortText);
        var longResult = PromptBudgetEstimator.Estimate(longText);

        longResult.Should().BeGreaterThan(shortResult);
    }

    [Fact]
    public void Estimate_MultipleTexts_SumsAllEstimates()
    {
        var text1 = "Hello";
        var text2 = "World";

        var combined = PromptBudgetEstimator.Estimate(text1, text2);
        var separate = PromptBudgetEstimator.Estimate(text1) + PromptBudgetEstimator.Estimate(text2);

        combined.Should().Be(separate);
    }

    [Fact]
    public void Estimate_SingleCharacter_ReturnsAtLeastOne()
    {
        PromptBudgetEstimator.Estimate("a").Should().BeGreaterThanOrEqualTo(1);
    }
}
