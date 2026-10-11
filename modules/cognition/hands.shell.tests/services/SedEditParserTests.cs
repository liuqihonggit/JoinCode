// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace Hands.Tests.Shell;

/// <summary>
/// SedEditParser 单元测试 — 验证 sed 编辑解析器的状态机解析、BRE→ERE 转换、
/// sed 替换→.NET 替换转换、标志校验、正则标志构建、Shell token 解析。
/// 对齐 TS sedEditParser.ts,纯计算确定性测试。
/// </summary>
public class SedEditParserTest {
    // ===== ParseSubstitutionExpression 状态机解析 =====

    [Fact]
    public void ParseSubstitutionExpression_BasicThreeSlash_ReturnsParsedInfo() {
        var info = SedEditParser.ParseSubstitutionExpression("s/a/b/", "file.txt", false);
        info.Should().NotBeNull();
        info!.FilePath.Should().Be("file.txt");
        info.Pattern.Should().Be("a");
        info.Replacement.Should().Be("b");
        info.Flags.Should().Be("");
        info.ExtendedRegex.Should().BeFalse();
    }

    [Fact]
    public void ParseSubstitutionExpression_WithGlobalFlag_ReturnsFlags() {
        var info = SedEditParser.ParseSubstitutionExpression("s/a/b/g", "f", false);
        info.Should().NotBeNull();
        info!.Flags.Should().Be("g");
    }

    [Fact]
    public void ParseSubstitutionExpression_WithMultipleFlags_ReturnsCombinedFlags() {
        var info = SedEditParser.ParseSubstitutionExpression("s/a/b/gi", "f", false);
        info.Should().NotBeNull();
        info!.Flags.Should().Be("gi");
    }

    [Fact]
    public void ParseSubstitutionExpression_ExtendedRegexPropagated() {
        var info = SedEditParser.ParseSubstitutionExpression("s/a/b/", "f", true);
        info.Should().NotBeNull();
        info!.ExtendedRegex.Should().BeTrue();
    }

    [Fact]
    public void ParseSubstitutionExpression_EscapedSlashInReplacement_PreservesBackslash() {
        // expression = s/a/b\/c/ → pattern=a, replacement=b\/c, flags=""
        // replacement 中的 \/ 是转义的 /,不切换状态
        var info = SedEditParser.ParseSubstitutionExpression("s/a/b\\/c/", "f", false);
        info.Should().NotBeNull();
        info!.Pattern.Should().Be("a");
        info.Replacement.Should().Be("b\\/c");
        info.Flags.Should().Be("");
    }

    [Fact]
    public void ParseSubstitutionExpression_EscapedSlashInPattern_PreservesBackslash() {
        // expression = "s/\//b/" → rest = "\//b/" → pattern=\/, replacement=b, flags=""
        var info = SedEditParser.ParseSubstitutionExpression("s/\\//b/", "f", false);
        info.Should().NotBeNull();
        info!.Pattern.Should().Be("\\/");
        info.Replacement.Should().Be("b");
    }

    [Theory]
    [InlineData("x/a/b/")]          // 不以 s/ 开头
    [InlineData("s/a/b")]           // 只有 1 个 /,状态停在 Replacement
    [InlineData("s/a/")]            // 只有 1 个 /,状态停在 Replacement
    [InlineData("s/")]              // rest 为空,状态停在 Pattern
    [InlineData("s/a/b/\\g")]       // flags 中转义 → null
    [InlineData("s/a/b/g/")]        // flags 后再遇 / → null
    [InlineData("s/a/b/x")]         // 无效标志 x
    [InlineData("s/a/b/0")]         // 0 不在 1-9 白名单
    public void ParseSubstitutionExpression_InvalidExpressions_ReturnsNull(string expr) {
        SedEditParser.ParseSubstitutionExpression(expr, "f", false).Should().BeNull();
    }

    [Fact]
    public void ParseSubstitutionExpression_NumericFlagAccepted() {
        var info = SedEditParser.ParseSubstitutionExpression("s/a/b/1", "f", false);
        info.Should().NotBeNull();
        info!.Flags.Should().Be("1");
    }

    // ===== ConvertBreToEre BRE→ERE 转义 8 种分支 =====

    [Theory]
    [InlineData("a\\+b", "a+b")]    // \+ → +
    [InlineData("a\\?b", "a?b")]    // \? → ?
    [InlineData("a\\(b\\)", "a(b)")] // \( \) → ( )
    [InlineData("a\\|b", "a|b")]    // \| → |
    [InlineData("a\\{1,2\\}", "a{1,2}")] // \{ \} → { }
    [InlineData("abc", "abc")]      // 普通字符不变
    [InlineData("\\d", "\\d")]      // \d 非特殊,保留 '\',然后 'd'
    [InlineData("a\\", "a\\")]      // 末尾单独 '\',走 else 分支追加 '\'
    public void ConvertBreToEre_EscapeBranches_ReturnsExpected(string input, string expected) {
        SedEditParser.ConvertBreToEre(input).Should().Be(expected);
    }

    [Theory]
    [InlineData("a+b", "a\\+b")]    // 未转义 + → \+
    [InlineData("a?b", "a\\?b")]    // 未转义 ? → \?
    [InlineData("a(b)", "a\\(b\\)")] // 未转义 ( ) → \( \)
    [InlineData("a|b", "a\\|b")]    // 未转义 | → \|
    [InlineData("a{1}", "a\\{1\\}")] // 未转义 { } → \{ \}
    public void ConvertBreToEre_UnescapedSpecialChars_EscapesThem(string input, string expected) {
        SedEditParser.ConvertBreToEre(input).Should().Be(expected);
    }

    // ===== ConvertSedReplacement sed 替换→.NET 替换 =====

    [Theory]
    [InlineData("a\\/b", "a/b")]    // \/ → /
    [InlineData("a\\&b", "a&b")]    // \& → &
    [InlineData("a\\nb", "a\nb")]   // \n → 换行
    [InlineData("a\\tb", "a\tb")]   // \t → 制表符
    [InlineData("abc", "abc")]      // 普通字符不变
    [InlineData("\\d", "\\d")]      // \d 非特殊,保留 '\',然后 'd'
    public void ConvertSedReplacement_EscapeBranches_ReturnsExpected(string input, string expected) {
        SedEditParser.ConvertSedReplacement(input).Should().Be(expected);
    }

    [Fact]
    public void ConvertSedReplacement_Ampersand_BecomesDollarAmpersand() {
        // & → $& (完整匹配)
        SedEditParser.ConvertSedReplacement("a&b").Should().Be("a$&b");
    }

    [Fact]
    public void ConvertSedReplacement_DollarSign_BecomesDoubleDollar() {
        // $ 在 .NET 替换中有特殊含义,需转义为 $$
        SedEditParser.ConvertSedReplacement("a$b").Should().Be("a$$b");
    }

    // ===== IsValidSedFlags 标志白名单校验 =====

    [Theory]
    [InlineData("", true)]          // 空标志合法
    [InlineData("g", true)]         // 全局替换
    [InlineData("p", true)]         // 打印
    [InlineData("i", true)]         // 忽略大小写
    [InlineData("I", true)]         // 忽略大小写(大写)
    [InlineData("m", true)]         // 多行
    [InlineData("M", true)]         // 多行(大写)
    [InlineData("1", true)]         // 数字 1-9
    [InlineData("9", true)]
    [InlineData("gi", true)]        // 组合
    [InlineData("g1", true)]
    [InlineData("x", false)]        // 非法字符
    [InlineData("0", false)]        // 0 不在 1-9
    [InlineData("gpimIM", true)]    // 全部合法字符
    [InlineData("gx", false)]       // 混合非法
    public void IsValidSedFlags_WhitelistCheck_ReturnsExpected(string flags, bool expected) {
        SedEditParser.IsValidSedFlags(flags).Should().Be(expected);
    }

    // ===== BuildRegexFlags sed flags→RegexOptions 映射 =====

    [Fact]
    public void BuildRegexFlags_Empty_ReturnsNone() {
        SedEditParser.BuildRegexFlags("").Should().Be(RegexOptions.None);
    }

    [Fact]
    public void BuildRegexFlags_GlobalFlag_ReturnsNone() {
        // g 在 .NET 中由 Replace 自动处理全局替换
        SedEditParser.BuildRegexFlags("g").Should().Be(RegexOptions.None);
    }

    [Theory]
    [InlineData("i", RegexOptions.IgnoreCase)]
    [InlineData("I", RegexOptions.IgnoreCase)]
    [InlineData("m", RegexOptions.Multiline)]
    [InlineData("M", RegexOptions.Multiline)]
    public void BuildRegexFlags_SingleFlag_ReturnsMappedOption(string flags, RegexOptions expected) {
        SedEditParser.BuildRegexFlags(flags).Should().Be(expected);
    }

    [Fact]
    public void BuildRegexFlags_CombinedFlags_ReturnsBitwiseOr() {
        SedEditParser.BuildRegexFlags("im").Should().Be(RegexOptions.IgnoreCase | RegexOptions.Multiline);
    }

    [Fact]
    public void BuildRegexFlags_IgnoreCaseWithGlobal_ReturnsOnlyIgnoreCase() {
        SedEditParser.BuildRegexFlags("gi").Should().Be(RegexOptions.IgnoreCase);
    }

    // ===== TryParseShellTokens Shell token 解析(单引号/双引号/转义) =====

    [Fact]
    public void TryParseShellTokens_EmptyString_ReturnsEmptyList() {
        var tokens = SedEditParser.TryParseShellTokens("");
        tokens.Should().NotBeNull().And.BeEmpty();
    }

    [Fact]
    public void TryParseShellTokens_SimpleSpaceSeparated_ReturnsTokens() {
        var tokens = SedEditParser.TryParseShellTokens("a b");
        tokens.Should().Equal(["a", "b"]);
    }

    [Fact]
    public void TryParseShellTokens_SingleQuote_PreservesSpaces() {
        var tokens = SedEditParser.TryParseShellTokens("'a b'");
        tokens.Should().Equal(["a b"]);
    }

    [Fact]
    public void TryParseShellTokens_DoubleQuote_PreservesSpaces() {
        var tokens = SedEditParser.TryParseShellTokens("\"a b\"");
        tokens.Should().Equal(["a b"]);
    }

    [Fact]
    public void TryParseShellTokens_EscapedSpace_BecomesPartOfToken() {
        // \ 转义空格 → 空格成为 token 一部分
        var tokens = SedEditParser.TryParseShellTokens("a\\ b");
        tokens.Should().Equal(["a b"]);
    }

    [Fact]
    public void TryParseShellTokens_MixedQuotesAndPlain_ReturnsCorrectTokens() {
        var tokens = SedEditParser.TryParseShellTokens("a 'b c' d");
        tokens.Should().Equal(["a", "b c", "d"]);
    }

    [Theory]
    [InlineData("'unclosed")]      // 未闭合单引号
    [InlineData("\"unclosed")]     // 未闭合双引号
    [InlineData("a 'b c")]         // 未闭合单引号(带前缀)
    public void TryParseShellTokens_UnclosedQuotes_ReturnsNull(string command) {
        SedEditParser.TryParseShellTokens(command).Should().BeNull();
    }

    [Fact]
    public void TryParseShellTokens_EscapeInsideDoubleQuote_BecomesLiteral() {
        // 双引号内 \ 转义: "a\\b" → a\b
        var tokens = SedEditParser.TryParseShellTokens("\"a\\\\b\"");
        tokens.Should().Equal(["a\\b"]);
    }

    [Fact]
    public void TryParseShellTokens_NoEscapeInsideSingleQuote_BecomesLiteralBackslash() {
        // 单引号内 \ 不转义: 'a\b' → a\b
        var tokens = SedEditParser.TryParseShellTokens("'a\\b'");
        tokens.Should().Equal(["a\\b"]);
    }

    [Fact]
    public void TryParseShellTokens_LeadingTrailingWhitespace_Trimmed() {
        var tokens = SedEditParser.TryParseShellTokens("  a b  ");
        tokens.Should().Equal(["a", "b"]);
    }
}
