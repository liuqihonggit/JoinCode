namespace Tools.Handlers.Tests;

public sealed class ToolDiagnosticMessageTests
{
    [Fact]
    public void BuildGlobNoResultMessage_NoWildcard_SuggestsRecursive()
    {
        var msg = SearchToolHandlers.BuildGlobNoResultMessage("exact_name.cs", null);

        msg.Should().StartWith("No files found");
        msg.Should().Contain("[诊断]");
        msg.Should().Contain("不含通配符");
        msg.Should().Contain("**/");
    }

    [Fact]
    public void BuildGlobNoResultMessage_WithWildcard_NoWildcardHint()
    {
        var msg = SearchToolHandlers.BuildGlobNoResultMessage("**/*.cs", null);

        msg.Should().StartWith("No files found");
        msg.Should().Contain("[诊断]");
        msg.Should().NotContain("不含通配符");
    }

    [Fact]
    public void BuildGrepNoResultMessage_UppercasePattern_SuggestsCaseInsensitive()
    {
        var msg = SearchToolHandlers.BuildGrepNoResultMessage("MyFunction", null, caseInsensitive: false);

        msg.Should().StartWith("No files found");
        msg.Should().Contain("[诊断]");
        msg.Should().Contain("case_insensitive");
        msg.Should().Contain("-i 选项");
    }

    [Fact]
    public void BuildGrepNoResultMessage_CaseInsensitive_NoHint()
    {
        var msg = SearchToolHandlers.BuildGrepNoResultMessage("MyFunction", null, caseInsensitive: true);

        msg.Should().StartWith("No files found");
        msg.Should().NotContain("-i 选项");
    }

    [Fact]
    public void BuildUnknownSettingMessage_ContainsAllSettings()
    {
        var msg = ConfigToolHandlers.BuildUnknownSettingMessage("nonexistent");

        msg.Should().StartWith("Unknown setting: \"nonexistent\"");
        msg.Should().Contain("[诊断]");
        msg.Should().Contain("支持的设置项");
    }

    [Fact]
    public void BuildUnknownSettingMessage_PartialMatch_SuggestsCandidate()
    {
        var msg = ConfigToolHandlers.BuildUnknownSettingMessage("theme");

        msg.Should().Contain("你是不是想用");
        msg.Should().Contain("theme");
    }

    [Fact]
    public void BuildUnknownSettingMessage_SubstringMatch_SuggestsCandidate()
    {
        var msg = ConfigToolHandlers.BuildUnknownSettingMessage("deb");

        msg.Should().Contain("你是不是想用");
        msg.Should().Contain("debuglog");
    }
}
