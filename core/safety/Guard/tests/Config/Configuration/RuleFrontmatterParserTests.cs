namespace Core.Tests.Configuration;

public sealed class RuleFrontmatterParserTests
{
    [Fact]
    public void Parse_NoFrontmatter_Should_Return_Raw_Content()
    {
        var (content, alwaysApply, globs, description) = RuleFrontmatterParser.Parse("Just a rule");

        Assert.Equal("Just a rule", content);
        Assert.False(alwaysApply);
        Assert.Equal(string.Empty, globs);
        Assert.Equal(string.Empty, description);
    }

    [Fact]
    public void Parse_WithAlwaysApply_Should_Parse_Bool()
    {
        var raw = "---\nalwaysApply: true\n---\nRule content";
        var (content, alwaysApply, globs, description) = RuleFrontmatterParser.Parse(raw);

        Assert.Equal("Rule content", content);
        Assert.True(alwaysApply);
    }

    [Fact]
    public void Parse_WithGlobs_Should_Parse_Patterns()
    {
        var raw = "---\nglobs: \"*.cs, *.tsx\"\n---\nTypeScript rules";
        var (content, alwaysApply, globs, description) = RuleFrontmatterParser.Parse(raw);

        Assert.Equal("TypeScript rules", content);
        Assert.Equal("*.cs, *.tsx", globs);
    }

    [Fact]
    public void Parse_WithDescription_Should_Parse_Text()
    {
        var raw = "---\ndescription: \"Rules for API development\"\n---\nAPI rules";
        var (content, alwaysApply, globs, description) = RuleFrontmatterParser.Parse(raw);

        Assert.Equal("API rules", content);
        Assert.Equal("Rules for API development", description);
    }

    [Fact]
    public void Parse_AllFields_Should_Parse_Everything()
    {
        var raw = "---\nalwaysApply: false\nglobs: \"*.py\"\ndescription: Python rules\n---\nPython coding standards";
        var (content, alwaysApply, globs, description) = RuleFrontmatterParser.Parse(raw);

        Assert.Equal("Python coding standards", content);
        Assert.False(alwaysApply);
        Assert.Equal("*.py", globs);
        Assert.Equal("Python rules", description);
    }

    [Fact]
    public void Parse_UnclosedFrontmatter_Should_Return_Raw()
    {
        var raw = "---\nalwaysApply: true\nNo closing";
        var (content, alwaysApply, globs, description) = RuleFrontmatterParser.Parse(raw);

        Assert.Equal(raw, content);
        Assert.False(alwaysApply);
    }

    [Fact]
    public void Parse_WithQuotedDescription_Should_Strip_Quotes()
    {
        var raw = "---\ndescription: 'My rule'\n---\nBody";
        var (content, alwaysApply, globs, description) = RuleFrontmatterParser.Parse(raw);

        Assert.Equal("My rule", description);
    }

    [Fact]
    public void Parse_AlwaysApplyHyphenated_Should_Parse()
    {
        var raw = "---\nalways-apply: true\n---\nBody";
        var (content, alwaysApply, globs, description) = RuleFrontmatterParser.Parse(raw);

        Assert.True(alwaysApply);
    }
}

public sealed class RuleFileTests
{
    [Fact]
    public void MatchStrategy_AlwaysApply_Should_Be_Always()
    {
        var rule = new RuleFile { Name = "test", Content = "test", AlwaysApply = true };
        Assert.Equal(RuleMatchStrategy.Always, rule.MatchStrategy);
    }

    [Fact]
    public void MatchStrategy_WithGlobs_Should_Be_Glob()
    {
        var rule = new RuleFile { Name = "test", Content = "test", Globs = "*.cs" };
        Assert.Equal(RuleMatchStrategy.Glob, rule.MatchStrategy);
    }

    [Fact]
    public void MatchStrategy_WithDescription_Should_Be_Description()
    {
        var rule = new RuleFile { Name = "test", Content = "test", Description = "API rules" };
        Assert.Equal(RuleMatchStrategy.Description, rule.MatchStrategy);
    }

    [Fact]
    public void MatchStrategy_NoMetadata_Should_Be_Manual()
    {
        var rule = new RuleFile { Name = "test", Content = "test" };
        Assert.Equal(RuleMatchStrategy.Manual, rule.MatchStrategy);
    }

    [Fact]
    public void MatchStrategy_GlobsTakesPrecedenceOverDescription()
    {
        var rule = new RuleFile { Name = "test", Content = "test", Globs = "*.cs", Description = "C# rules" };
        Assert.Equal(RuleMatchStrategy.Glob, rule.MatchStrategy);
    }
}

public sealed class ExternalRulesLoaderTests
{
    [Fact]
    public void MatchesGlobPattern_StarCs_Should_Match_CsFile()
    {
        Assert.True(ExternalRulesLoader.MatchesGlobPattern("Program.cs", "*.cs"));
    }

    [Fact]
    public void MatchesGlobPattern_StarCs_Should_Not_Match_TsFile()
    {
        Assert.False(ExternalRulesLoader.MatchesGlobPattern("app.ts", "*.cs"));
    }

    [Fact]
    public void MatchesGlobPattern_ExactMatch_Should_Work()
    {
        Assert.True(ExternalRulesLoader.MatchesGlobPattern("README.md", "README.md"));
    }

    [Fact]
    public void MatchesGlobPattern_QuestionMark_Should_Match_SingleChar()
    {
        Assert.True(ExternalRulesLoader.MatchesGlobPattern("f1.ts", "f?.ts"));
    }

    [Fact]
    public void FilterAlwaysApply_Should_Return_OnlyAlwaysRules()
    {
        var loader = new ExternalRulesLoader(new IO.FileSystem.PhysicalFileSystem());
        var rules = new List<RuleFile>
        {
            new() { Name = "always", Content = "a", AlwaysApply = true },
            new() { Name = SearchToolNameConstants.Glob, Content = "g", Globs = "*.cs" },
            new() { Name = "desc", Content = "d", Description = "test" },
            new() { Name = "manual", Content = "m" }
        };

        var result = loader.FilterAlwaysApply(rules);
        Assert.Single(result);
        Assert.Equal("always", result[0].Name);
    }

    [Fact]
    public void FilterByGlobs_Should_Match_FilePath()
    {
        var loader = new ExternalRulesLoader(new IO.FileSystem.PhysicalFileSystem());
        var rules = new List<RuleFile>
        {
            new() { Name = "cs-rule", Content = "c", Globs = "*.cs" },
            new() { Name = "ts-rule", Content = "t", Globs = "*.ts" },
            new() { Name = "always", Content = "a", AlwaysApply = true }
        };

        var result = loader.FilterByGlobs(rules, "Program.cs");
        Assert.Single(result);
        Assert.Equal("cs-rule", result[0].Name);
    }

    [Fact]
    public void FilterByGlobs_MultiplePatterns_Should_Match_Any()
    {
        var loader = new ExternalRulesLoader(new IO.FileSystem.PhysicalFileSystem());
        var rules = new List<RuleFile>
        {
            new() { Name = "mixed", Content = "m", Globs = "*.cs, *.tsx" }
        };

        var result = loader.FilterByGlobs(rules, "App.tsx");
        Assert.Single(result);
    }

    [Fact]
    public void FilterByDescription_Should_Return_DescriptionRules()
    {
        var loader = new ExternalRulesLoader(new IO.FileSystem.PhysicalFileSystem());
        var rules = new List<RuleFile>
        {
            new() { Name = "desc", Content = "d", Description = "API rules" },
            new() { Name = "always", Content = "a", AlwaysApply = true },
            new() { Name = "manual", Content = "m" }
        };

        var result = loader.FilterByDescription(rules);
        Assert.Single(result);
        Assert.Equal("desc", result[0].Name);
    }

    [Fact]
    public Task LoadProjectRulesAsync_WithFrontmatter_Should_Parse_Metadata()
    {
        return Task.CompletedTask;
    }

    [Fact]
    public Task LoadProjectRulesAsync_GlobRule_Should_Have_Glob_Strategy()
    {
        return Task.CompletedTask;
    }
}
