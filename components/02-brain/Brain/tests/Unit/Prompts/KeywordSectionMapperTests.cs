using Core.Prompts.Utils;
using FluentAssertions;

namespace Core.Tests.Prompts;

public class KeywordSectionMapperTests
{
    [Fact]
    public void GetSectionContent_StructuredTaskWorkflow_ReturnsContent()
    {
        var content = KeywordSectionMapper.GetSectionContentForKeywordType(UserPromptKeywordType.StructuredTaskWorkflow);

        content.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetSectionContent_PerformanceAudit_ReturnsContent()
    {
        var content = KeywordSectionMapper.GetSectionContentForKeywordType(UserPromptKeywordType.PerformanceAudit);

        content.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetSectionContent_ReplacementMethodology_ReturnsContent()
    {
        var content = KeywordSectionMapper.GetSectionContentForKeywordType(UserPromptKeywordType.ReplacementMethodology);

        content.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetSectionContent_DeadlockAudit_ReturnsContent()
    {
        var content = KeywordSectionMapper.GetSectionContentForKeywordType(UserPromptKeywordType.DeadlockAudit);

        content.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetSectionContent_Consolidation_ReturnsContent()
    {
        var content = KeywordSectionMapper.GetSectionContentForKeywordType(UserPromptKeywordType.Consolidation);

        content.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetSectionContent_CompetitiveEdge_ReturnsContent()
    {
        var content = KeywordSectionMapper.GetSectionContentForKeywordType(UserPromptKeywordType.CompetitiveEdge);

        content.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void GetSectionContent_None_ReturnsNull()
    {
        var content = KeywordSectionMapper.GetSectionContentForKeywordType(UserPromptKeywordType.None);

        content.Should().BeNull();
    }

    [Fact]
    public void GetSectionContent_Negative_ReturnsNull()
    {
        var content = KeywordSectionMapper.GetSectionContentForKeywordType(UserPromptKeywordType.Negative);

        content.Should().BeNull();
    }

    [Fact]
    public void GetSectionContent_KeepGoing_ReturnsNull()
    {
        var content = KeywordSectionMapper.GetSectionContentForKeywordType(UserPromptKeywordType.KeepGoing);

        content.Should().BeNull();
    }
}
