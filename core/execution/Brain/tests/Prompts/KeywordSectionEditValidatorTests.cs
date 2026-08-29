namespace Core.Tests.Prompts;

public class KeywordSectionEditValidatorTests
{
    [Fact]
    public void ValidateEdit_ValidPath_ValidJson_ReturnsNull()
    {
        var original = """{"sections":{"fact_inquiry":{"keywords":["写一"],"enabled":true}}}""";
        var updated = """{"sections":{"fact_inquiry":{"keywords":["写一","分析"],"enabled":true}}}""";

        var result = KeywordSectionEditValidator.ValidateEdit(
            "C:\\Users\\user\\.jcc\\keyword-sections.json", original, updated);

        result.Should().BeNull();
    }

    [Fact]
    public void ValidateEdit_WrongPath_ReturnsError()
    {
        var result = KeywordSectionEditValidator.ValidateEdit(
            "C:\\Users\\user\\some-other-file.json", "{}", "{}");

        result.Should().NotBeNull();
        result.Should().Contain("只能编辑");
    }

    [Fact]
    public void ValidateEdit_EmptyContent_ReturnsError()
    {
        var result = KeywordSectionEditValidator.ValidateEdit(
            "C:\\Users\\user\\.jcc\\keyword-sections.json", "{}", "");

        result.Should().NotBeNull();
        result.Should().Contain("禁止清空");
    }

    [Fact]
    public void ValidateEdit_InvalidJson_ReturnsError()
    {
        var result = KeywordSectionEditValidator.ValidateEdit(
            "C:\\Users\\user\\.jcc\\keyword-sections.json", "{}", "not json");

        result.Should().NotBeNull();
        result.Should().Contain("格式非法");
    }

    [Fact]
    public void ValidateEdit_SectionDeleted_ReturnsError()
    {
        var original = """{"sections":{"fact_inquiry":{"keywords":["写一"],"enabled":true},"user_delegation":{"keywords":["睡觉"],"enabled":true}}}""";
        var updated = """{"sections":{"fact_inquiry":{"keywords":["写一"],"enabled":true}}}""";

        var result = KeywordSectionEditValidator.ValidateEdit(
            "C:\\Users\\user\\.jcc\\keyword-sections.json", original, updated);

        result.Should().NotBeNull();
        result.Should().Contain("禁止删除");
    }

    [Fact]
    public void ValidateEdit_SectionAdded_ReturnsNull()
    {
        var original = """{"sections":{"fact_inquiry":{"keywords":["写一"],"enabled":true}}}""";
        var updated = """{"sections":{"fact_inquiry":{"keywords":["写一"],"enabled":true},"user_delegation":{"keywords":["睡觉"],"enabled":true}}}""";

        var result = KeywordSectionEditValidator.ValidateEdit(
            "C:\\Users\\user\\.jcc\\keyword-sections.json", original, updated);

        result.Should().BeNull();
    }

    [Fact]
    public void IsKeywordSectionsPath_ValidPath_ReturnsTrue()
    {
        KeywordSectionEditValidator.IsKeywordSectionsPath("C:\\Users\\user\\.jcc\\keyword-sections.json").Should().BeTrue();
        KeywordSectionEditValidator.IsKeywordSectionsPath("/home/user/.jcc/keyword-sections.json").Should().BeTrue();
    }

    [Fact]
    public void IsKeywordSectionsPath_InvalidPath_ReturnsFalse()
    {
        KeywordSectionEditValidator.IsKeywordSectionsPath("C:\\Users\\user\\settings.json").Should().BeFalse();
        KeywordSectionEditValidator.IsKeywordSectionsPath("").Should().BeFalse();
    }
}
