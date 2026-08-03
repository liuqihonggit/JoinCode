namespace Core.Tests.Prompts;

public class HandsPromptTemplatesTests
{
    [Fact]
    public void GetContent_ReturnsCodeGenerationTemplate()
    {
        var content = HandsPromptTemplates.GetContent("code_generation");

        content.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetContent_ReturnsCodeAnalysisTemplate()
    {
        var content = HandsPromptTemplates.GetContent("code_analysis");

        content.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetAllTemplates_ContainsBothCodeTemplates()
    {
        var templates = HandsPromptTemplates.GetAllTemplates().ToList();

        templates.Should().Contain(t => t.Name == "code_generation" && !t.HasParameters);
        templates.Should().Contain(t => t.Name == "code_analysis" && !t.HasParameters);
    }
}
