using Core.Prompts;
using JoinCode.Abstractions.Prompts;
using FluentAssertions;

namespace Core.Tests.Prompts;

public class PromptSectionRegistrationTests
{
    [Fact]
    public void GetAlwaysSections_ReturnsNonEmpty()
    {
        var sections = PromptSectionRegistration.GetAlwaysSections();

        sections.Should().NotBeEmpty();
    }

    [Fact]
    public void GetAlwaysSections_ContainsIntro()
    {
        var sections = PromptSectionRegistration.GetAlwaysSections();

        sections.Should().Contain(s => s.Name == "intro");
    }

    [Fact]
    public void GetAlwaysSections_ContainsSystem()
    {
        var sections = PromptSectionRegistration.GetAlwaysSections();

        sections.Should().Contain(s => s.Name == "system");
    }

    [Fact]
    public void GetAgentModeSections_ReturnsNonEmpty()
    {
        var sections = PromptSectionRegistration.GetAgentModeSections();

        sections.Should().NotBeEmpty();
    }

    [Fact]
    public void GetAgentModeSections_ContainsCompetitiveEdge()
    {
        var sections = PromptSectionRegistration.GetAgentModeSections();

        sections.Should().Contain(s => s.Name == "competitive_edge");
    }

    [Fact]
    public void GetCoordinatorModeSections_ReturnsNonEmpty()
    {
        var sections = PromptSectionRegistration.GetCoordinatorModeSections();

        sections.Should().NotBeEmpty();
    }
}
