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

    [Fact]
    public void TryMatch_RepeatedCalls_SameConfig_ReturnsConsistentResult()
    {
        var config = CreateConfig("test", ["分析", "总结", "规划"]);
        var input = "帮我分析一下数据";

        var result1 = DynamicKeywordMatcher.TryMatch(input, config);
        var result2 = DynamicKeywordMatcher.TryMatch(input, config);
        var result3 = DynamicKeywordMatcher.TryMatch(input, config);

        result1.Should().NotBeNull();
        result2.Should().NotBeNull();
        result3.Should().NotBeNull();
        result1!.MatchedKeyword.Should().Be(result2!.MatchedKeyword).And.Be(result3!.MatchedKeyword);
        result1.SectionName.Should().Be(result2.SectionName).And.Be(result3.SectionName);
    }

    [Fact]
    public void TryMatch_DifferentConfigs_CacheIsolation()
    {
        var config1 = CreateConfig("section_a", ["分析"]);
        var config2 = CreateConfig("section_b", ["睡觉"]);

        var result1 = DynamicKeywordMatcher.TryMatch("帮我分析", config1);
        var result2 = DynamicKeywordMatcher.TryMatch("我去睡觉", config2);

        result1.Should().NotBeNull();
        result1!.SectionName.Should().Be("section_a");
        result1.MatchedKeyword.Should().Be("分析");

        result2.Should().NotBeNull();
        result2!.SectionName.Should().Be("section_b");
        result2.MatchedKeyword.Should().Be("睡觉");
    }

    [Fact]
    public void TryMatch_LargeConfig_RepeatedCalls_PreserveCorrectness()
    {
        var keywords = new List<string> { "写一", "做一", "分析", "总结", "规划", "生成", "重构", "修复", "添加", "修改", "实现", "替换", "归纳", "合并", "整理" };
        var config = CreateConfig("large_section", keywords);

        for (var i = 0; i < 5; i++)
        {
            var result = DynamicKeywordMatcher.TryMatch("帮我分析并总结数据", config);
            result.Should().NotBeNull();
            result!.SectionName.Should().Be("large_section");
        }
    }

    private static DynamicKeywordConfig CreateConfig(string sectionName, List<string> keywords) => new()
    {
        Sections = new Dictionary<string, DynamicKeywordSection>(StringComparer.OrdinalIgnoreCase)
        {
            [sectionName] = new() { Keywords = keywords, Enabled = true }
        }
    };
}
