namespace Core.Tests.Prompts;

[Trait("Category", "Unit")]
public class CompactPromptTemplateTests
{
    private const string ConstraintsSection = "关键约束与决策";
    private const string RisksSection = "风险与注意事项";
    private const string HandoffPathSection = "建议接手路径";

    [Fact]
    public void GetCompactPrompt_ContainsConstraintsAndDecisionsSection()
    {
        var prompt = CompactPromptTemplate.GetCompactPrompt();

        prompt.Should().Contain(ConstraintsSection);
    }

    [Fact]
    public void GetCompactPrompt_ContainsRisksAndCautionsSection()
    {
        var prompt = CompactPromptTemplate.GetCompactPrompt();

        prompt.Should().Contain(RisksSection);
    }

    [Fact]
    public void GetCompactPrompt_ContainsHandoffPathSection()
    {
        var prompt = CompactPromptTemplate.GetCompactPrompt();

        prompt.Should().Contain(HandoffPathSection);
    }

    [Fact]
    public void GetPartialCompactPrompt_ContainsThreeHandoffSections()
    {
        var prompt = CompactPromptTemplate.GetPartialCompactPrompt();

        prompt.Should().Contain(ConstraintsSection);
        prompt.Should().Contain(RisksSection);
        prompt.Should().Contain(HandoffPathSection);
    }

    [Fact]
    public void GetPartialCompactPrompt_UpToDirection_ContainsThreeHandoffSections()
    {
        var prompt = CompactPromptTemplate.GetPartialCompactPrompt(direction: CompactDirection.UpTo);

        prompt.Should().Contain(ConstraintsSection);
        prompt.Should().Contain(RisksSection);
        prompt.Should().Contain(HandoffPathSection);
    }
}
