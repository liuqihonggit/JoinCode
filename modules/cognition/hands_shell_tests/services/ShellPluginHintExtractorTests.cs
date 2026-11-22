namespace Hands.Tests.Shell;

/// <summary>
/// ShellPluginHintExtractor 单元测试 — 验证插件提示提取、XML 属性解析、
/// 首 token 提取、空行折叠。对齐 TS shellPluginHint,纯计算确定性测试。
/// </summary>
public class ShellPluginHintExtractorTest {
    private const string HintTag = "claude-code-hint";

    // ===== Extract 插件提示提取 =====

    [Fact]
    public void Extract_EmptyOutput_ReturnsEmptyHintsAndEmptyStripped() {
        var result = ShellPluginHintExtractor.Extract("", "git");
        result.Hints.Should().BeEmpty();
        result.StrippedOutput.Should().Be("");
    }

    [Fact]
    public void Extract_OutputWithoutHintTag_ReturnsOriginalOutput() {
        var output = "some normal output\nwithout hints";
        var result = ShellPluginHintExtractor.Extract(output, "git");
        result.Hints.Should().BeEmpty();
        result.StrippedOutput.Should().Be(output);
    }

    [Fact]
    public void Extract_ValidHint_ReturnsHintAndStripsTag() {
        var output = $"<{HintTag} v=\"1\" type=\"plugin\" value=\"myval\" />";
        var result = ShellPluginHintExtractor.Extract(output, "git status");
        result.Hints.Should().HaveCount(1);
        result.Hints[0].V.Should().Be(1);
        result.Hints[0].Type.Should().Be("plugin");
        result.Hints[0].Value.Should().Be("myval");
        result.Hints[0].SourceCommand.Should().Be("git");
        result.StrippedOutput.Should().Be("");
    }

    [Fact]
    public void Extract_HintSurroundedByText_StripsOnlyTag() {
        var output = $"before\n<{HintTag} v=\"1\" type=\"plugin\" value=\"x\" />\nafter";
        var result = ShellPluginHintExtractor.Extract(output, "cmd");
        result.Hints.Should().HaveCount(1);
        result.StrippedOutput.Should().Be("before\n\nafter");
    }

    [Fact]
    public void Extract_UnsupportedVersion_ReturnsNoHintButStrips() {
        var output = $"<{HintTag} v=\"2\" type=\"plugin\" value=\"x\" />";
        var result = ShellPluginHintExtractor.Extract(output, "cmd");
        result.Hints.Should().BeEmpty();
        result.StrippedOutput.Should().Be("");
    }

    [Fact]
    public void Extract_UnsupportedType_ReturnsNoHintButStrips() {
        var output = $"<{HintTag} v=\"1\" type=\"other\" value=\"x\" />";
        var result = ShellPluginHintExtractor.Extract(output, "cmd");
        result.Hints.Should().BeEmpty();
    }

    [Fact]
    public void Extract_EmptyValue_ReturnsNoHintButStrips() {
        var output = $"<{HintTag} v=\"1\" type=\"plugin\" value=\"\" />";
        var result = ShellPluginHintExtractor.Extract(output, "cmd");
        result.Hints.Should().BeEmpty();
    }

    [Fact]
    public void Extract_MultipleHints_ReturnsAll() {
        var output = $"<{HintTag} v=\"1\" type=\"plugin\" value=\"a\" />\n<{HintTag} v=\"1\" type=\"plugin\" value=\"b\" />";
        var result = ShellPluginHintExtractor.Extract(output, "cmd");
        result.Hints.Should().HaveCount(2);
        result.Hints[0].Value.Should().Be("a");
        result.Hints[1].Value.Should().Be("b");
    }

    [Fact]
    public void Extract_UnquotedAttrs_ParsesCorrectly() {
        var output = $"<{HintTag} v=1 type=plugin value=unquoted />";
        var result = ShellPluginHintExtractor.Extract(output, "cmd");
        result.Hints.Should().HaveCount(1);
        result.Hints[0].Value.Should().Be("unquoted");
    }

    [Fact]
    public void Extract_LeadingTrailingSpacesAroundTag_Matched() {
        var output = $"  <{HintTag} v=\"1\" type=\"plugin\" value=\"x\" />  ";
        var result = ShellPluginHintExtractor.Extract(output, "cmd");
        result.Hints.Should().HaveCount(1);
    }

    [Fact]
    public void Extract_EmptyCommand_SourceCommandIsEmpty() {
        var output = $"<{HintTag} v=\"1\" type=\"plugin\" value=\"x\" />";
        var result = ShellPluginHintExtractor.Extract(output, "");
        result.Hints.Should().HaveCount(1);
        result.Hints[0].SourceCommand.Should().Be("");
    }

    // ===== ParseAttrs XML 属性解析 =====

    [Fact]
    public void ParseAttrs_QuotedValues_ReturnsAllAttrs() {
        var attrs = ShellPluginHintExtractor.ParseAttrs($"<{HintTag} v=\"1\" type=\"plugin\" value=\"x\" />");
        attrs["v"].Should().Be("1");
        attrs["type"].Should().Be("plugin");
        attrs["value"].Should().Be("x");
    }

    [Fact]
    public void ParseAttrs_UnquotedValues_ReturnsAllAttrs() {
        var attrs = ShellPluginHintExtractor.ParseAttrs($"<{HintTag} v=1 type=plugin value=x />");
        attrs["v"].Should().Be("1");
        attrs["type"].Should().Be("plugin");
        attrs["value"].Should().Be("x");
    }

    [Fact]
    public void ParseAttrs_MixedQuotedAndUnquoted_ReturnsAllAttrs() {
        var attrs = ShellPluginHintExtractor.ParseAttrs($"<{HintTag} v=\"1\" type=plugin value=\"x\" />");
        attrs["v"].Should().Be("1");
        attrs["type"].Should().Be("plugin");
        attrs["value"].Should().Be("x");
    }

    [Fact]
    public void ParseAttrs_NoAttrs_ReturnsEmpty() {
        var attrs = ShellPluginHintExtractor.ParseAttrs($"<{HintTag} />");
        attrs.Should().BeEmpty();
    }

    [Fact]
    public void ParseAttrs_EmptyString_ReturnsEmpty() {
        var attrs = ShellPluginHintExtractor.ParseAttrs("");
        attrs.Should().BeEmpty();
    }

    // ===== FirstCommandToken 首 token 提取 =====

    [Theory]
    [InlineData("", "")]                    // 空字符串
    [InlineData("git", "git")]              // 无空格
    [InlineData("git status", "git")]       // 单空格
    [InlineData("  git status", "git")]     // 前导空格
    [InlineData("git status -a", "git")]    // 多参数
    [InlineData("   ", "")]                 // 仅空格
    public void FirstCommandToken_VariousInputs_ReturnsExpected(string command, string expected) {
        ShellPluginHintExtractor.FirstCommandToken(command).Should().Be(expected);
    }

    // ===== CollapseExcessiveBlankLines 空行折叠 =====

    [Fact]
    public void CollapseExcessiveBlankLines_ThreeOrMoreNewlines_FoldsToTwo() {
        ShellPluginHintExtractor.CollapseExcessiveBlankLines("a\n\n\n\nb").Should().Be("a\n\nb");
    }

    [Fact]
    public void CollapseExcessiveBlankLines_TwoNewlines_Unchanged() {
        ShellPluginHintExtractor.CollapseExcessiveBlankLines("a\n\nb").Should().Be("a\n\nb");
    }

    [Fact]
    public void CollapseExcessiveBlankLines_SingleNewline_Unchanged() {
        ShellPluginHintExtractor.CollapseExcessiveBlankLines("a\nb").Should().Be("a\nb");
    }

    [Fact]
    public void CollapseExcessiveBlankLines_EmptyString_Unchanged() {
        ShellPluginHintExtractor.CollapseExcessiveBlankLines("").Should().Be("");
    }

    [Fact]
    public void CollapseExcessiveBlankLines_OnlyNewlines_FoldsToTwo() {
        ShellPluginHintExtractor.CollapseExcessiveBlankLines("\n\n\n").Should().Be("\n\n");
    }

    [Fact]
    public void CollapseExcessiveBlankLines_MultipleSections_FoldsEach() {
        ShellPluginHintExtractor.CollapseExcessiveBlankLines("a\n\n\nb\n\n\nc").Should().Be("a\n\nb\n\nc");
    }

    [Fact]
    public void CollapseExcessiveBlankLines_NoNewlines_Unchanged() {
        ShellPluginHintExtractor.CollapseExcessiveBlankLines("abcdef").Should().Be("abcdef");
    }
}
