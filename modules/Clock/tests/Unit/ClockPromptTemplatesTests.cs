namespace Clock.Tests;

public class ClockPromptTemplatesTests
{
    [Fact]
    public void GetAllTemplates_ContainsContinuation()
    {
        var templates = ClockPromptTemplates.GetAllTemplates().ToList();

        var continuation = templates.FirstOrDefault(t => t.Name == "continuation");
        continuation.Should().NotBeNull();
        continuation!.Category.Should().Be("Goal");
        continuation.HasParameters.Should().BeTrue();
    }

    [Fact]
    public void GetContent_ReturnsNullForParameterizedTemplate()
    {
        var content = ClockPromptTemplates.GetContent("continuation");

        content.Should().BeNull();
    }
}
