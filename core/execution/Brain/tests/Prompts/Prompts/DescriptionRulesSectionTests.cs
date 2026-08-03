namespace Core.Tests.Prompts;

public sealed class DescriptionRulesSectionTests
{
    [Fact]
    public void Create_With_Null_Rules_Should_Return_Null_Content()
    {
        var tracker = new FileContextTracker();
        tracker.UpdateUserMessage("修复bug");
        PromptConfigSnapshot.SetCurrent(new SystemPromptProviderOptions { ExternalRules = null, FileContext = tracker });

        var section = DescriptionRulesSection.Create();

        section.Compute().Should().BeNull();
    }

    [Fact]
    public void Create_With_Empty_Rules_Should_Return_Null_Content()
    {
        var tracker = new FileContextTracker();
        tracker.UpdateUserMessage("修复bug");
        PromptConfigSnapshot.SetCurrent(new SystemPromptProviderOptions { ExternalRules = [], FileContext = tracker });

        var section = DescriptionRulesSection.Create();

        section.Compute().Should().BeNull();
    }

    [Fact]
    public void Create_With_No_User_Message_Should_Return_Null_Content()
    {
        var tracker = new FileContextTracker();
        var rules = new List<ExternalRuleEntry>
        {
            new() { Name = "测试", Content = "规则内容", Description = "修复bug时使用" }
        };
        PromptConfigSnapshot.SetCurrent(new SystemPromptProviderOptions { ExternalRules = rules, FileContext = tracker });

        var section = DescriptionRulesSection.Create();

        section.Compute().Should().BeNull();
    }

    [Fact]
    public void Create_With_Matching_Description_Should_Return_Content()
    {
        var tracker = new FileContextTracker();
        tracker.UpdateUserMessage("修复登录页面的bug");

        var rules = new List<ExternalRuleEntry>
        {
            new() { Name = "Bug修复规则", Content = "修复bug时请先写测试", Description = "修复bug时使用" }
        };
        PromptConfigSnapshot.SetCurrent(new SystemPromptProviderOptions { ExternalRules = rules, FileContext = tracker });

        var section = DescriptionRulesSection.Create();
        var content = section.Compute();

        content.Should().NotBeNull();
        content.Should().Contain("Bug修复规则");
        content.Should().Contain("修复bug时请先写测试");
        content.Should().Contain("修复bug时使用");
    }

    [Fact]
    public void Create_With_Non_Matching_Description_Should_Return_Null()
    {
        var tracker = new FileContextTracker();
        tracker.UpdateUserMessage("添加新功能");

        var rules = new List<ExternalRuleEntry>
        {
            new() { Name = "Bug修复规则", Content = "修复bug时请先写测试", Description = "修复bug时使用" }
        };
        PromptConfigSnapshot.SetCurrent(new SystemPromptProviderOptions { ExternalRules = rules, FileContext = tracker });

        var section = DescriptionRulesSection.Create();

        section.Compute().Should().BeNull();
    }

    [Fact]
    public void Create_Should_Skip_AlwaysApply_Rules()
    {
        var tracker = new FileContextTracker();
        tracker.UpdateUserMessage("修复bug");

        var rules = new List<ExternalRuleEntry>
        {
            new() { Name = "始终规则", Content = "始终生效", AlwaysApply = true, Description = "修复bug" }
        };
        PromptConfigSnapshot.SetCurrent(new SystemPromptProviderOptions { ExternalRules = rules, FileContext = tracker });

        var section = DescriptionRulesSection.Create();

        section.Compute().Should().BeNull();
    }

    [Fact]
    public void Create_Should_Skip_Glob_Rules()
    {
        var tracker = new FileContextTracker();
        tracker.UpdateUserMessage("修复bug");

        var rules = new List<ExternalRuleEntry>
        {
            new() { Name = "Glob规则", Content = "文件相关", Globs = "*.cs", Description = "修复bug" }
        };
        PromptConfigSnapshot.SetCurrent(new SystemPromptProviderOptions { ExternalRules = rules, FileContext = tracker });

        var section = DescriptionRulesSection.Create();

        section.Compute().Should().BeNull();
    }

    [Fact]
    public void Create_Should_Skip_Rule_Without_Description()
    {
        var tracker = new FileContextTracker();
        tracker.UpdateUserMessage("修复bug");

        var rules = new List<ExternalRuleEntry>
        {
            new() { Name = "无描述规则", Content = "规则内容", Description = "" }
        };
        PromptConfigSnapshot.SetCurrent(new SystemPromptProviderOptions { ExternalRules = rules, FileContext = tracker });

        var section = DescriptionRulesSection.Create();

        section.Compute().Should().BeNull();
    }

    [Fact]
    public void Create_Should_Be_Dynamic_Section()
    {
        var tracker = new FileContextTracker();
        PromptConfigSnapshot.SetCurrent(new SystemPromptProviderOptions { ExternalRules = null, FileContext = tracker });

        var section = DescriptionRulesSection.Create();

        section.CacheBreak.Should().BeTrue();
    }

    [Fact]
    public void Create_Should_Reflect_Updated_Message()
    {
        var tracker = new FileContextTracker();
        var rules = new List<ExternalRuleEntry>
        {
            new() { Name = "Bug修复规则", Content = "修复bug时请先写测试", Description = "修复bug时使用" }
        };
        PromptConfigSnapshot.SetCurrent(new SystemPromptProviderOptions { ExternalRules = rules, FileContext = tracker });

        var section = DescriptionRulesSection.Create();

        section.Compute().Should().BeNull();

        tracker.UpdateUserMessage("修复登录bug");
        var content = section.Compute();

        content.Should().NotBeNull();
        content.Should().Contain("Bug修复规则");
    }

    [Fact]
    public void Create_With_English_Description_Should_Match()
    {
        var tracker = new FileContextTracker();
        tracker.UpdateUserMessage("Fix the authentication bug");

        var rules = new List<ExternalRuleEntry>
        {
            new() { Name = "Auth Rule", Content = "Check auth flow", Description = "authentication bug fix" }
        };
        PromptConfigSnapshot.SetCurrent(new SystemPromptProviderOptions { ExternalRules = rules, FileContext = tracker });

        var section = DescriptionRulesSection.Create();
        var content = section.Compute();

        content.Should().NotBeNull();
        content.Should().Contain("Auth Rule");
    }

    [Fact]
    public void Create_With_Partial_Keyword_Match_Should_Work()
    {
        var tracker = new FileContextTracker();
        tracker.UpdateUserMessage("优化性能问题");

        var rules = new List<ExternalRuleEntry>
        {
            new() { Name = "性能规则", Content = "使用Span优化", Description = "性能优化相关" }
        };
        PromptConfigSnapshot.SetCurrent(new SystemPromptProviderOptions { ExternalRules = rules, FileContext = tracker });

        var section = DescriptionRulesSection.Create();
        var content = section.Compute();

        content.Should().NotBeNull();
        content.Should().Contain("性能规则");
    }

    [Fact]
    public void Create_With_Multiple_Matching_Rules_Should_Return_All()
    {
        var tracker = new FileContextTracker();
        tracker.UpdateUserMessage("修复安全漏洞");

        var rules = new List<ExternalRuleEntry>
        {
            new() { Name = "安全规则A", Content = "检查输入验证", Description = "安全漏洞修复" },
            new() { Name = "安全规则B", Content = "使用参数化查询", Description = "安全漏洞修复" }
        };
        PromptConfigSnapshot.SetCurrent(new SystemPromptProviderOptions { ExternalRules = rules, FileContext = tracker });

        var section = DescriptionRulesSection.Create();
        var content = section.Compute();

        content.Should().NotBeNull();
        content.Should().Contain("安全规则A");
        content.Should().Contain("安全规则B");
    }

    [Fact]
    public void ExtractKeywords_Should_Filter_Stop_Words()
    {
        var keywords = DescriptionRulesSection.ExtractKeywords("这是一个 修复 的 方案");

        keywords.Should().NotContain("的");
        keywords.Should().NotContain("是");
    }

    [Fact]
    public void ExtractKeywords_Should_Filter_Short_Words()
    {
        var keywords = DescriptionRulesSection.ExtractKeywords("a bug fix");

        keywords.Should().NotContain("a");
        keywords.Should().Contain("bug");
        keywords.Should().Contain("fix");
    }

    [Fact]
    public void ExtractKeywords_Should_Deduplicate()
    {
        var keywords = DescriptionRulesSection.ExtractKeywords("bug bug fix fix");

        keywords.Should().HaveCount(2);
    }

    [Fact]
    public void ExtractKeywords_Should_Extract_CJK_Bigrams()
    {
        var keywords = DescriptionRulesSection.ExtractKeywords("修复登录页面的bug");

        keywords.Should().Contain("修复");
        keywords.Should().Contain("登录");
        keywords.Should().Contain("页面");
        keywords.Should().Contain("bug");
    }

    [Fact]
    public void Create_With_Empty_User_Message_Should_Return_Null()
    {
        var tracker = new FileContextTracker();
        tracker.UpdateUserMessage("");

        var rules = new List<ExternalRuleEntry>
        {
            new() { Name = "规则", Content = "内容", Description = "描述" }
        };
        PromptConfigSnapshot.SetCurrent(new SystemPromptProviderOptions { ExternalRules = rules, FileContext = tracker });

        var section = DescriptionRulesSection.Create();

        section.Compute().Should().BeNull();
    }
}
