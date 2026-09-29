namespace Guard.Hooks.Tests;

/// <summary>
/// PermissionCheckContext 静态/内部纯函数确定性测试 — 路径模式匹配、Span 序列匹配、工具类型判定。
/// <para>IsSensitivePath 使用 Path.GetFullPath(确定性 IO),ResolveSpecialFolder 使用 Environment.GetFolderPath(确定性 IO)。</para>
/// </summary>
public class PermissionCheckContextInternalTests {

    #region MatchesPattern

    [Theory]
    [InlineData("hello world", "world", PatternType.Contains, true)]
    [InlineData("hello world", "hello", PatternType.StartsWith, true)]
    [InlineData("hello world", "world", PatternType.EndsWith, true)]
    [InlineData("hello", "hello", PatternType.Exact, true)]
    [InlineData("hello world", "WORLD", PatternType.Contains, true)]    // 大小写不敏感
    [InlineData("hello world", "goodbye", PatternType.Contains, false)]
    [InlineData("hello", "world", PatternType.Exact, false)]
    public void MatchesPattern_VariousInputs(string input, string pattern, PatternType type, bool expected) {
        PermissionCheckContext.MatchesPattern(input, pattern, type).Should().Be(expected);
    }

    [Fact]
    public void MatchesPattern_Regex_Should_Match() {
        PermissionCheckContext.MatchesPattern("test123", @"\d+", PatternType.Regex).Should().BeTrue();
        PermissionCheckContext.MatchesPattern("test", @"\d+", PatternType.Regex).Should().BeFalse();
    }

    [Fact]
    public void MatchesPattern_EmptyInput_Should_Return_False() {
        PermissionCheckContext.MatchesPattern("", "pattern", PatternType.Contains).Should().BeFalse();
        PermissionCheckContext.MatchesPattern(null!, "pattern", PatternType.Contains).Should().BeFalse();
    }

    #endregion

    #region IsDangerousCommand

    [Fact]
    public void IsDangerousCommand_MatchingPattern_Should_Return_True() {
        var patterns = new List<DangerousCommandPattern> {
            new() { Pattern = "rm -rf" },
            new() { Pattern = "format" }
        };
        PermissionCheckContext.IsDangerousCommand("rm -rf /", patterns).Should().BeTrue();
        PermissionCheckContext.IsDangerousCommand("format c:", patterns).Should().BeTrue();
    }

    [Fact]
    public void IsDangerousCommand_NoMatch_Should_Return_False() {
        var patterns = new List<DangerousCommandPattern> {
            new() { Pattern = "rm -rf" }
        };
        PermissionCheckContext.IsDangerousCommand("ls -la", patterns).Should().BeFalse();
    }

    [Fact]
    public void IsDangerousCommand_EmptyPatterns_Should_Return_False() {
        PermissionCheckContext.IsDangerousCommand("rm -rf /", new List<DangerousCommandPattern>()).Should().BeFalse();
    }

    [Fact]
    public void IsDangerousCommand_CaseInsensitive_Should_Match() {
        var patterns = new List<DangerousCommandPattern> {
            new() { Pattern = "RM -RF" }
        };
        PermissionCheckContext.IsDangerousCommand("rm -rf /", patterns).Should().BeTrue();
    }

    #endregion

    #region IsSensitivePath

    [Fact]
    public void IsSensitivePath_StartsWith_Should_Match() {
        var patterns = new List<SensitivePathPattern> {
            new() { Path = "C:\\Windows", PathType = PathType.StartsWith }
        };
        PermissionCheckContext.IsSensitivePath("C:\\Windows\\System32", patterns).Should().BeTrue();
    }

    [Fact]
    public void IsSensitivePath_Contains_Should_Match() {
        var patterns = new List<SensitivePathPattern> {
            new() { Path = "secret", PathType = PathType.Contains }
        };
        PermissionCheckContext.IsSensitivePath("C:\\project\\secret\\file.txt", patterns).Should().BeTrue();
    }

    [Fact]
    public void IsSensitivePath_NoMatch_Should_Return_False() {
        var patterns = new List<SensitivePathPattern> {
            new() { Path = "C:\\Windows", PathType = PathType.StartsWith }
        };
        PermissionCheckContext.IsSensitivePath("C:\\project\\file.txt", patterns).Should().BeFalse();
    }

    [Fact]
    public void IsSensitivePath_EmptyPatterns_Should_Return_False() {
        PermissionCheckContext.IsSensitivePath("C:\\anywhere", new List<SensitivePathPattern>()).Should().BeFalse();
    }

    #endregion

    #region IsFileReadTool / IsFileWriteTool

    [Fact]
    public void IsFileReadTool_ReadTool_Should_Be_True() {
        PermissionCheckContext.IsFileReadTool(FileToolNameEnumConstants.FileRead).Should().BeTrue();
        PermissionCheckContext.IsFileReadTool(SearchToolNameEnumConstants.Grep).Should().BeTrue();
        PermissionCheckContext.IsFileReadTool(SearchToolNameEnumConstants.Glob).Should().BeTrue();
    }

    [Fact]
    public void IsFileReadTool_NonReadTool_Should_Be_False() {
        PermissionCheckContext.IsFileReadTool(FileToolNameEnumConstants.FileWrite).Should().BeFalse();
        PermissionCheckContext.IsFileReadTool("unknown_tool").Should().BeFalse();
    }

    [Fact]
    public void IsFileReadTool_CaseInsensitive_Should_Be_True() {
        PermissionCheckContext.IsFileReadTool(FileToolNameEnumConstants.FileRead.ToUpperInvariant()).Should().BeTrue();
    }

    [Fact]
    public void IsFileWriteTool_WriteTools_Should_Be_True() {
        PermissionCheckContext.IsFileWriteTool(FileToolNameEnumConstants.FileWrite).Should().BeTrue();
        PermissionCheckContext.IsFileWriteTool(FileToolNameEnumConstants.FileEdit).Should().BeTrue();
        PermissionCheckContext.IsFileWriteTool(FileToolNameEnumConstants.FileEditRegex).Should().BeTrue();
    }

    [Fact]
    public void IsFileWriteTool_NonWriteTool_Should_Be_False() {
        PermissionCheckContext.IsFileWriteTool(FileToolNameEnumConstants.FileRead).Should().BeFalse();
        PermissionCheckContext.IsFileWriteTool("unknown_tool").Should().BeFalse();
    }

    #endregion

    #region IsConfigGetOperation

    [Fact]
    public void IsConfigGetOperation_ConfigGet_Should_Be_True() {
        PermissionCheckContext.IsConfigGetOperation(ConfigToolNameEnumConstants.ConfigGet, null).Should().BeTrue();
        PermissionCheckContext.IsConfigGetOperation(ConfigToolNameEnumConstants.ConfigList, null).Should().BeTrue();
    }

    [Fact]
    public void IsConfigGetOperation_ConfigWithNullValue_Should_Be_True() {
        // Config 工具无 arguments 或无 value 键 → 视为 GET 操作
        PermissionCheckContext.IsConfigGetOperation(ConfigToolNameEnumConstants.Config, null).Should().BeTrue();
    }

    [Fact]
    public void IsConfigGetOperation_ConfigWithSetValue_Should_Be_False() {
        var args = new Dictionary<string, JsonElement> {
            ["value"] = JsonSerializer.SerializeToElement("somevalue")
        };
        PermissionCheckContext.IsConfigGetOperation(ConfigToolNameEnumConstants.Config, args).Should().BeFalse();
    }

    [Fact]
    public void IsConfigGetOperation_NonConfigTool_Should_Be_False() {
        PermissionCheckContext.IsConfigGetOperation("unknown_tool", null).Should().BeFalse();
    }

    #endregion

    #region ContainsOrdinalIgnoreCase / MatchesOrdinalIgnoreCase

    [Theory]
    [InlineData("Hello World", "world", true)]
    [InlineData("Hello World", "hello", true)]
    [InlineData("Hello World", "goodbye", false)]
    [InlineData("Hello", "Hello World", false)]    // value 比 source 长
    [InlineData("Hello", "", true)]                 // 空 value 总是匹配
    public void ContainsOrdinalIgnoreCase_VariousInputs(string source, string value, bool expected) {
        PermissionCheckContext.ContainsOrdinalIgnoreCase(source.AsSpan(), value.AsSpan()).Should().Be(expected);
    }

    [Fact]
    public void ContainsOrdinalIgnoreCase_EmptySource_Should_Return_False() {
        PermissionCheckContext.ContainsOrdinalIgnoreCase("".AsSpan(), "x".AsSpan()).Should().BeFalse();
    }

    [Theory]
    [InlineData("Hello", "Hello", true)]
    [InlineData("hello", "HELLO", true)]            // 大小写不敏感
    [InlineData("Hello", "World", false)]
    [InlineData("Hello", "Helloo", false)]           // 长度不同
    public void MatchesOrdinalIgnoreCase_VariousInputs(string a, string b, bool expected) {
        PermissionCheckContext.MatchesOrdinalIgnoreCase(a.AsSpan(), b.AsSpan()).Should().Be(expected);
    }

    #endregion

    #region ResolveSpecialFolder

    [Fact]
    public void ResolveSpecialFolder_UnknownPlaceholder_Should_Return_As_Is() {
        PermissionCheckContext.ResolveSpecialFolder("{Unknown}").Should().Be("{Unknown}");
    }

    [Fact]
    public void ResolveSpecialFolder_PlainPath_Should_Return_As_Is() {
        PermissionCheckContext.ResolveSpecialFolder("C:\\project").Should().Be("C:\\project");
    }

    [Fact]
    public void ResolveSpecialFolder_Windows_Should_Resolve_To_System_Path() {
        var resolved = PermissionCheckContext.ResolveSpecialFolder("{Windows}");
        resolved.Should().NotBe("{Windows}");
        resolved.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ResolveSpecialFolder_System_Should_Resolve_To_System_Path() {
        var resolved = PermissionCheckContext.ResolveSpecialFolder("{System}");
        resolved.Should().NotBe("{System}");
        resolved.Should().NotBeNullOrEmpty();
    }

    #endregion
}
