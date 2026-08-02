using Core.Prompts.Sections;
using Core.Prompts.Utils;
using FluentAssertions;

namespace Core.Tests.Prompts;

public class DynamicKeywordSectionTests
{
    [Fact]
    public void FactInquirySection_GetContent_ReturnsNonEmpty()
    {
        var content = FactInquirySection.GetContent();
        content.Should().NotBeNullOrEmpty();
        content.Should().Contain("事实完整性");
    }

    [Fact]
    public void UserDelegationSection_GetContent_ReturnsNonEmpty()
    {
        var content = UserDelegationSection.GetContent();
        content.Should().NotBeNullOrEmpty();
        content.Should().Contain("自主决策");
    }

    [Fact]
    public void KeywordSectionMapper_GetSectionContentForName_FactInquiry_ReturnsContent()
    {
        var content = KeywordSectionMapper.GetSectionContentForName("fact_inquiry");
        content.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void KeywordSectionMapper_GetSectionContentForName_UserDelegation_ReturnsContent()
    {
        var content = KeywordSectionMapper.GetSectionContentForName("user_delegation");
        content.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void KeywordSectionMapper_GetSectionContentForName_Unknown_ReturnsNull()
    {
        var content = KeywordSectionMapper.GetSectionContentForName("nonexistent_section");
        content.Should().BeNull();
    }

    [Fact]
    public void KeywordSectionMapper_GetSectionContentForName_CaseInsensitive_ReturnsContent()
    {
        var content = KeywordSectionMapper.GetSectionContentForName("FACT_INQUIRY");
        content.Should().NotBeNullOrEmpty();
    }
}
