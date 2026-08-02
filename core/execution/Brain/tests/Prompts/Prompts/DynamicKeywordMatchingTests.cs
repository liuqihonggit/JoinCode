using Core.Prompts.Utils;
using FluentAssertions;

namespace Core.Tests.Prompts;

public class DynamicKeywordMatchingTests
{
    [Fact]
    public void TryMatch_FactInquiryKeyword_ReturnsMatch()
    {
        var config = CreateConfig("fact_inquiry", ["写一", "分析", "总结"]);
        var result = DynamicKeywordMatcher.TryMatch("帮我写一份周报", config);

        result.Should().NotBeNull();
        result!.SectionName.Should().Be("fact_inquiry");
        result.MatchedKeyword.Should().Be("写一");
    }

    [Fact]
    public void TryMatch_UserDelegationKeyword_ReturnsMatch()
    {
        var config = CreateConfig("user_delegation", ["睡觉", "离开", "走了"]);
        var result = DynamicKeywordMatcher.TryMatch("我睡觉去了", config);

        result.Should().NotBeNull();
        result!.SectionName.Should().Be("user_delegation");
        result.MatchedKeyword.Should().Be("睡觉");
    }

    [Fact]
    public void TryMatch_NoMatch_ReturnsNull()
    {
        var config = CreateConfig("fact_inquiry", ["写一", "分析"]);
        var result = DynamicKeywordMatcher.TryMatch("今天天气怎么样", config);

        result.Should().BeNull();
    }

    [Fact]
    public void TryMatch_DisabledSection_ReturnsNull()
    {
        var config = new DynamicKeywordConfig
        {
            Sections = new Dictionary<string, DynamicKeywordSection>(StringComparer.OrdinalIgnoreCase)
            {
                ["fact_inquiry"] = new() { Keywords = ["写一"], Enabled = false }
            }
        };
        var result = DynamicKeywordMatcher.TryMatch("帮我写一份周报", config);

        result.Should().BeNull();
    }

    [Fact]
    public void TryMatch_EmptyInput_ReturnsNull()
    {
        var config = CreateConfig("fact_inquiry", ["写一"]);
        DynamicKeywordMatcher.TryMatch("", config).Should().BeNull();
        DynamicKeywordMatcher.TryMatch(null!, config).Should().BeNull();
        DynamicKeywordMatcher.TryMatch("   ", config).Should().BeNull();
    }

    [Fact]
    public void TryMatch_CustomContent_ReturnsCustomContent()
    {
        var config = new DynamicKeywordConfig
        {
            Sections = new Dictionary<string, DynamicKeywordSection>(StringComparer.OrdinalIgnoreCase)
            {
                ["custom_section"] = new() { Keywords = ["自定义"], Enabled = true, CustomContent = "自定义注入内容" }
            }
        };
        var result = DynamicKeywordMatcher.TryMatch("这是一个自定义请求", config);

        result.Should().NotBeNull();
        result!.HasCustomContent.Should().BeTrue();
        result.CustomContent.Should().Be("自定义注入内容");
    }

    [Fact]
    public void TryMatch_CaseInsensitive_Matches()
    {
        var config = CreateConfig("test", ["Chrome"]);
        var result = DynamicKeywordMatcher.TryMatch("打开chrome浏览器", config);

        result.Should().NotBeNull();
        result!.MatchedKeyword.Should().Be("Chrome");
    }

    [Fact]
    public void TryMatch_MinimalKeywordRoot_MatchesVariant()
    {
        var config = CreateConfig("user_delegation", ["睡觉"]);
        DynamicKeywordMatcher.TryMatch("我去睡觉了", config).Should().NotBeNull();
        DynamicKeywordMatcher.TryMatch("我睡觉去了", config).Should().NotBeNull();
        DynamicKeywordMatcher.TryMatch("先睡觉", config).Should().NotBeNull();
    }

    [Fact]
    public void TryMatch_MultipleSections_FirstMatchWins()
    {
        var config = new DynamicKeywordConfig
        {
            Sections = new Dictionary<string, DynamicKeywordSection>(StringComparer.OrdinalIgnoreCase)
            {
                ["fact_inquiry"] = new() { Keywords = ["分析"], Enabled = true },
                ["user_delegation"] = new() { Keywords = ["睡觉"], Enabled = true }
            }
        };
        var result = DynamicKeywordMatcher.TryMatch("帮我分析一下", config);

        result.Should().NotBeNull();
        result!.SectionName.Should().Be("fact_inquiry");
    }

    private static DynamicKeywordConfig CreateConfig(string sectionName, List<string> keywords) => new()
    {
        Sections = new Dictionary<string, DynamicKeywordSection>(StringComparer.OrdinalIgnoreCase)
        {
            [sectionName] = new() { Keywords = keywords, Enabled = true }
        }
    };
}
