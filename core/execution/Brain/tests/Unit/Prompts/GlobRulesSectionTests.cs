namespace Core.Tests.Prompts;

public sealed class GlobRulesSectionTests
{
    [Fact]
    public void Create_With_Null_Rules_Should_Return_Null_Content()
    {
        var tracker = new FileContextTracker();
        tracker.UpdateFilePaths(["test.cs"]);
        PromptConfigSnapshot.SetCurrent(new SystemPromptProviderOptions { ExternalRules = null, FileContext = tracker });

        var section = GlobRulesSection.Create();

        section.Compute().Should().BeNull();
    }

    [Fact]
    public void Create_With_Empty_Rules_Should_Return_Null_Content()
    {
        var tracker = new FileContextTracker();
        tracker.UpdateFilePaths(["test.cs"]);
        PromptConfigSnapshot.SetCurrent(new SystemPromptProviderOptions { ExternalRules = [], FileContext = tracker });

        var section = GlobRulesSection.Create();

        section.Compute().Should().BeNull();
    }

    [Fact]
    public void Create_With_No_File_Context_Should_Return_Null_Content()
    {
        var tracker = new FileContextTracker();
        var rules = new List<ExternalRuleEntry>
        {
            new() { Name = "test", Content = "rule content", Globs = "*.cs" }
        };
        PromptConfigSnapshot.SetCurrent(new SystemPromptProviderOptions { ExternalRules = rules, FileContext = tracker });

        var section = GlobRulesSection.Create();

        section.Compute().Should().BeNull();
    }

    [Fact]
    public void Create_With_Matching_Glob_Should_Return_Content()
    {
        var tracker = new FileContextTracker();
        tracker.UpdateFilePaths(["Program.cs"]);

        var rules = new List<ExternalRuleEntry>
        {
            new() { Name = "C# 规则", Content = "使用 var 关键字", Globs = "*.cs" }
        };
        PromptConfigSnapshot.SetCurrent(new SystemPromptProviderOptions { ExternalRules = rules, FileContext = tracker });

        var section = GlobRulesSection.Create();
        var content = section.Compute();

        content.Should().NotBeNull();
        content.Should().Contain("C# 规则");
        content.Should().Contain("使用 var 关键字");
    }

    [Fact]
    public void Create_With_Non_Matching_Glob_Should_Return_Null()
    {
        var tracker = new FileContextTracker();
        tracker.UpdateFilePaths(["README.md"]);

        var rules = new List<ExternalRuleEntry>
        {
            new() { Name = "C# 规则", Content = "使用 var 关键字", Globs = "*.cs" }
        };
        PromptConfigSnapshot.SetCurrent(new SystemPromptProviderOptions { ExternalRules = rules, FileContext = tracker });

        var section = GlobRulesSection.Create();

        section.Compute().Should().BeNull();
    }

    [Fact]
    public void Create_With_Multiple_Patterns_Should_Match_Any()
    {
        var tracker = new FileContextTracker();
        tracker.UpdateFilePaths(["app.ts"]);

        var rules = new List<ExternalRuleEntry>
        {
            new() { Name = "前端规则", Content = "使用严格模式", Globs = "*.js, *.ts, *.tsx" }
        };
        PromptConfigSnapshot.SetCurrent(new SystemPromptProviderOptions { ExternalRules = rules, FileContext = tracker });

        var section = GlobRulesSection.Create();
        var content = section.Compute();

        content.Should().NotBeNull();
        content.Should().Contain("前端规则");
    }

    [Fact]
    public void Create_With_Description_Should_Include_Description()
    {
        var tracker = new FileContextTracker();
        tracker.UpdateFilePaths(["test.cs"]);

        var rules = new List<ExternalRuleEntry>
        {
            new() { Name = "C# 规则", Content = "规则内容", Globs = "*.cs", Description = "适用于C#文件" }
        };
        PromptConfigSnapshot.SetCurrent(new SystemPromptProviderOptions { ExternalRules = rules, FileContext = tracker });

        var section = GlobRulesSection.Create();
        var content = section.Compute();

        content.Should().Contain("适用于C#文件");
    }

    [Fact]
    public void Create_With_Rule_Without_Globs_Should_Be_Skipped()
    {
        var tracker = new FileContextTracker();
        tracker.UpdateFilePaths(["test.cs"]);

        var rules = new List<ExternalRuleEntry>
        {
            new() { Name = "Always规则", Content = "始终生效", Globs = "" },
            new() { Name = "Glob规则", Content = "文件相关", Globs = "*.cs" }
        };
        PromptConfigSnapshot.SetCurrent(new SystemPromptProviderOptions { ExternalRules = rules, FileContext = tracker });

        var section = GlobRulesSection.Create();
        var content = section.Compute();

        content.Should().NotBeNull();
        content.Should().Contain("Glob规则");
        content.Should().NotContain("Always规则");
    }

    [Fact]
    public void Create_Should_Match_Full_Path()
    {
        var tracker = new FileContextTracker();
        tracker.UpdateFilePaths(["src/core/Program.cs"]);

        var rules = new List<ExternalRuleEntry>
        {
            new() { Name = "Core规则", Content = "核心模块规范", Globs = "src/core/**" }
        };
        PromptConfigSnapshot.SetCurrent(new SystemPromptProviderOptions { ExternalRules = rules, FileContext = tracker });

        var section = GlobRulesSection.Create();
        var content = section.Compute();

        content.Should().NotBeNull();
        content.Should().Contain("Core规则");
    }

    [Fact]
    public void Create_Should_Be_Dynamic_Section()
    {
        var tracker = new FileContextTracker();
        PromptConfigSnapshot.SetCurrent(new SystemPromptProviderOptions { ExternalRules = null, FileContext = tracker });

        var section = GlobRulesSection.Create();

        section.CacheBreak.Should().BeTrue();
    }

    [Fact]
    public void Create_Should_Reflect_Updated_File_Context()
    {
        var tracker = new FileContextTracker();
        var rules = new List<ExternalRuleEntry>
        {
            new() { Name = "C# 规则", Content = "规则内容", Globs = "*.cs" }
        };
        PromptConfigSnapshot.SetCurrent(new SystemPromptProviderOptions { ExternalRules = rules, FileContext = tracker });

        var section = GlobRulesSection.Create();

        section.Compute().Should().BeNull();

        tracker.UpdateFilePaths(["Program.cs"]);
        var content = section.Compute();

        content.Should().NotBeNull();
        content.Should().Contain("C# 规则");
    }

    [Fact]
    public void Create_With_Exact_Name_Match_Should_Work()
    {
        var tracker = new FileContextTracker();
        tracker.UpdateFilePaths(["Program.cs"]);

        var rules = new List<ExternalRuleEntry>
        {
            new() { Name = "特定文件", Content = "仅Program.cs", Globs = "Program.cs" }
        };
        PromptConfigSnapshot.SetCurrent(new SystemPromptProviderOptions { ExternalRules = rules, FileContext = tracker });

        var section = GlobRulesSection.Create();
        var content = section.Compute();

        content.Should().NotBeNull();
        content.Should().Contain("特定文件");
    }

    [Fact]
    public void Create_With_Question_Mark_Wildcard_Should_Work()
    {
        var tracker = new FileContextTracker();
        tracker.UpdateFilePaths(["test.cs"]);

        var rules = new List<ExternalRuleEntry>
        {
            new() { Name = "短名规则", Content = "短文件名", Globs = "????.cs" }
        };
        PromptConfigSnapshot.SetCurrent(new SystemPromptProviderOptions { ExternalRules = rules, FileContext = tracker });

        var section = GlobRulesSection.Create();
        var content = section.Compute();

        content.Should().NotBeNull();
        content.Should().Contain("短名规则");
    }
}
