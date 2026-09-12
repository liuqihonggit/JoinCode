namespace Core.Tests.Prompts;

public class BrainPromptTemplatesTests
{
    [Fact]
    public void GetContent_ReturnsPromptSuggestionTemplate()
    {
        var content = BrainPromptTemplates.GetContent("prompt_suggestion");

        content.Should().NotBeNullOrEmpty();
        content.Should().Contain("建议模式");
    }

    [Fact]
    public void GetContent_ReturnsNullForParameterizedTemplate()
    {
        var content = BrainPromptTemplates.GetContent("compact");

        content.Should().BeNull();
    }

    [Fact]
    public void GetContent_ReturnsNullForUnknownTemplate()
    {
        var content = BrainPromptTemplates.GetContent("nonexistent");

        content.Should().BeNull();
    }

    [Fact]
    public void GetAllTemplates_ReturnsAllRegisteredTemplates()
    {
        var templates = BrainPromptTemplates.GetAllTemplates().ToList();

        templates.Should().NotBeEmpty();
        templates.Should().Contain(t => t.Name == "prompt_suggestion");
        templates.Should().Contain(t => t.Name == "compact");
    }

    [Fact]
    public void GetAllTemplates_ContainsCategoryAndDescription()
    {
        var templates = BrainPromptTemplates.GetAllTemplates().ToList();

        var promptSuggestion = templates.First(t => t.Name == "prompt_suggestion");
        promptSuggestion.Category.Should().Be("System");
        promptSuggestion.Description.Should().NotBeEmpty();
        promptSuggestion.HasParameters.Should().BeFalse();
    }

    [Fact]
    public void GetAllTemplates_ParameterizedTemplateHasHasParametersTrue()
    {
        var templates = BrainPromptTemplates.GetAllTemplates().ToList();

        var compact = templates.First(t => t.Name == "compact");
        compact.HasParameters.Should().BeTrue();
        compact.Category.Should().Be("Memory");
    }
}
