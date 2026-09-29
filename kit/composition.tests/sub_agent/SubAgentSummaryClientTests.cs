namespace Core.Tests.SubAgent;

/// <summary>
/// SubAgentSummaryClient.BuildSystemPrompt 确定性测试 — 纯字符串构建，参数化覆盖边界。
/// </summary>
public sealed class SubAgentSummaryClientTests {
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(100)]
    [InlineData(4096)]
    [InlineData(int.MaxValue)]
    public void BuildSystemPrompt_PositiveOrZero_ContainsTokenValue(int maxOutputTokens) {
        var prompt = SubAgentSummaryClient.BuildSystemPrompt(maxOutputTokens);
        prompt.Should().Contain($"不超过 {maxOutputTokens} token");
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-100)]
    [InlineData(int.MinValue)]
    public void BuildSystemPrompt_Negative_ContainsNegativeValue(int maxOutputTokens) {
        var prompt = SubAgentSummaryClient.BuildSystemPrompt(maxOutputTokens);
        prompt.Should().Contain($"不超过 {maxOutputTokens} token");
    }

    [Fact]
    public void BuildSystemPrompt_AlwaysContainsKeyPhrase() {
        var prompt = SubAgentSummaryClient.BuildSystemPrompt(100);
        prompt.Should().Contain("摘要助手");
        prompt.Should().Contain("保留关键信息");
        prompt.Should().Contain("直接输出摘要内容");
    }

    [Fact]
    public void BuildSystemPrompt_DifferentValues_ProduceDifferentPrompts() {
        var prompt1 = SubAgentSummaryClient.BuildSystemPrompt(100);
        var prompt2 = SubAgentSummaryClient.BuildSystemPrompt(200);
        prompt1.Should().NotBe(prompt2);
    }

    [Fact]
    public void BuildSystemPrompt_SameValue_ProducesSamePrompt() {
        var prompt1 = SubAgentSummaryClient.BuildSystemPrompt(500);
        var prompt2 = SubAgentSummaryClient.BuildSystemPrompt(500);
        prompt1.Should().Be(prompt2);
    }
}
