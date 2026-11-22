namespace JoinCode.Security.Tests.Permission;

/// <summary>
/// TASK070 P1-P2/DSG046 — 权限规则串解析器测试（Parse/Format 双向往返 + 前缀匹配 + 命令提取）。
/// 规则串三形态（对齐 TS permissionRuleParser.ts）："Bash" 工具级 / "WebFetch(domain:x)" 精确 / "Bash(npm run:*)" 前缀。
/// </summary>
public sealed class PermissionRuleParserTests {
    [Theory]
    [InlineData("Bash", "Bash", null, false)]
    [InlineData("WebFetch(domain:example.com)", "WebFetch", "domain:example.com", false)]
    [InlineData("Bash(npm run:*)", "Bash", "npm run", true)]
    public void Parse_AllThreeForms_DecodesCorrectly(string ruleValue, string toolName, string? content, bool isPrefix) {
        var parsed = PermissionRuleParser.Parse(ruleValue);

        Assert.NotNull(parsed);
        Assert.Equal((toolName, content, isPrefix), (parsed.ToolName, parsed.Content, parsed.IsPrefix));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("(noTool)")]
    public void Parse_InvalidValue_ReturnsNull(string? ruleValue) {
        Assert.Null(PermissionRuleParser.Parse(ruleValue));
    }

    [Fact]
    public void Format_RoundTrip_WithParse_IsConsistent() {
        // Parse(Format(x)) == x — 双向同一实现（禁止双向维护契约）
        Assert.Equal("Bash", PermissionRuleParser.Format("Bash"));
        Assert.Equal("WebFetch(domain:example.com)", PermissionRuleParser.Format("WebFetch", "domain:example.com"));
        Assert.Equal("Bash(npm run:*)", PermissionRuleParser.Format("Bash", "npm run", isPrefix: true));

        var parsed = PermissionRuleParser.Parse(PermissionRuleParser.Format("Bash", "npm run", isPrefix: true));
        Assert.NotNull(parsed);
        Assert.Equal(("Bash", "npm run", true), (parsed.ToolName, parsed.Content, parsed.IsPrefix));
    }

    [Theory]
    [InlineData("npm run", true, "npm run build", true)]
    [InlineData("npm run", true, "npm run", true)]
    [InlineData("npm run", true, "npmtest", false)]
    [InlineData("npm run", true, null, false)]
    [InlineData("git status", false, "git status", true)]
    [InlineData("git status", false, "git status --short", false)]
    [InlineData("git status", false, "", false)]
    public void MatchesContent_PrefixAndExact_Semantics(string ruleContent, bool isPrefix, string? command, bool expected) {
        Assert.Equal(expected, PermissionRuleParser.MatchesContent(ruleContent, isPrefix, command));
    }

    [Fact]
    public void ExtractCommand_CommandField_ReturnsTrimmed() {
        var arguments = new Dictionary<string, JsonElement> {
            ["command"] = JsonDocument.Parse("\"  npm run build  \"").RootElement.Clone(),
        };

        Assert.Equal("npm run build", PermissionRuleParser.ExtractCommand(arguments));
    }

    [Fact]
    public void ExtractCommand_MissingOrNonString_ReturnsNull() {
        Assert.Null(PermissionRuleParser.ExtractCommand(new Dictionary<string, JsonElement>()));
        Assert.Null(PermissionRuleParser.ExtractCommand(new Dictionary<string, JsonElement> {
            ["path"] = JsonDocument.Parse("\"a.cs\"").RootElement.Clone(),
        }));
        Assert.Null(PermissionRuleParser.ExtractCommand(null));
    }
}
