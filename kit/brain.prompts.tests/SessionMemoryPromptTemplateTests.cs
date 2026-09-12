namespace Core.Tests.Prompts;

[Trait("Category", "Unit")]
public class SessionMemoryPromptTemplateTests
{
    [Fact]
    public void DefaultTemplate_LearningsSection_ContainsVerifiedDeadEndsGuidance()
    {
        var template = SessionMemoryPromptTemplate.DefaultSessionMemoryTemplate;

        template.Should().Contain("已验证过且不建议继续");
    }

    [Fact]
    public void DefaultTemplate_LearningsSection_ContainsMisjudgmentGuidance()
    {
        var template = SessionMemoryPromptTemplate.DefaultSessionMemoryTemplate;

        template.Should().Contain("容易误判");
    }
}
