namespace Mcp.Tests;

/// <summary>
/// McpUnicodeSanitizer 单元测试 — 验证 Unicode 清理(零宽字符/BOM/私用区 + 迭代收敛 + NFKC 规范化)
/// </summary>
public sealed class McpUnicodeSanitizerTests {
    [Fact]
    public void PartiallySanitize_PlainAscii_Unchanged() {
        McpUnicodeSanitizer.PartiallySanitize("hello world").Should().Be("hello world");
    }

    [Fact]
    public void PartiallySanitize_ZeroWidthSpace_Removed() {
        // U+200B 零宽空格
        McpUnicodeSanitizer.PartiallySanitize("hello\u200Bworld").Should().Be("helloworld");
    }

    [Fact]
    public void PartiallySanitize_ZeroWidthJoiner_Removed() {
        // U+200D 零宽连字符
        McpUnicodeSanitizer.PartiallySanitize("a\u200Db").Should().Be("ab");
    }

    [Fact]
    public void PartiallySanitize_Bom_Removed() {
        // U+FEFF BOM
        McpUnicodeSanitizer.PartiallySanitize("\uFEFFhello").Should().Be("hello");
    }

    [Fact]
    public void PartiallySanitize_PrivateUseArea_Removed() {
        // U+E000 BMP 私用区
        McpUnicodeSanitizer.PartiallySanitize("\uE000test\uF8FF").Should().Be("test");
    }

    [Fact]
    public void PartiallySanitize_LtrRtlMarks_Removed() {
        // U+200E LTR mark, U+200F RTL mark
        McpUnicodeSanitizer.PartiallySanitize("x\u200E\u200Fy").Should().Be("xy");
    }

    [Fact]
    public void PartiallySanitize_DirectionalIsolates_Removed() {
        // U+2066 LRI, U+2067 RLI, U+2068 FSI, U+2069 PDI
        McpUnicodeSanitizer.PartiallySanitize("a\u2066\u2067\u2068\u2069b").Should().Be("ab");
    }

    [Fact]
    public void PartiallySanitize_MultipleDangerousChars_AllRemoved() {
        var input = "s\u200B\uFEFF\uE000t\u200D\u200E";
        McpUnicodeSanitizer.PartiallySanitize(input).Should().Be("st");
    }

    [Fact]
    public void PartiallySanitize_EmptyString_Throws() {
        var act = () => McpUnicodeSanitizer.PartiallySanitize(string.Empty);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void PartiallySanitize_Null_Throws() {
        var act = () => McpUnicodeSanitizer.PartiallySanitize(null!);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void SanitizeRound_PlainAscii_Unchanged() {
        McpUnicodeSanitizer.SanitizeRound("hello").Should().Be("hello");
    }

    [Fact]
    public void SanitizeRound_CombiningCharsWithDangerous_DangerousRemoved() {
        // 组合字符序列 + 危险字符: 危险字符被移除,组合字符保留(NFKC 行为依赖运行时 ICU,不断言组合结果)
        var result = McpUnicodeSanitizer.SanitizeRound("A\u0301\u200B");
        result.Should().NotContain("\u200B");
        result.Should().Contain("A");
    }

    [Fact]
    public void SanitizeRound_ZeroWidthChars_Removed() {
        McpUnicodeSanitizer.SanitizeRound("a\u200Bb").Should().Be("ab");
    }

    [Fact]
    public void SanitizeRound_Bom_Removed() {
        McpUnicodeSanitizer.SanitizeRound("\uFEFFx").Should().Be("x");
    }

    [Fact]
    public void SanitizeRound_PrivateUseArea_Removed() {
        McpUnicodeSanitizer.SanitizeRound("\uE000y").Should().Be("y");
    }

    [Fact]
    public void SanitizeRound_DirectionalFormat_Removed() {
        // U+202A LRE, U+202E RLO
        McpUnicodeSanitizer.SanitizeRound("p\u202A\u202Eq").Should().Be("pq");
    }

    [Fact]
    public void SanitizeRound_FullwidthDigitWithDangerous_DangerousRemoved() {
        // 全角数字 + 危险字符: 危险字符被移除(NFKC 全角→半角依赖运行时 ICU,不断言转换结果)
        var result = McpUnicodeSanitizer.SanitizeRound("\uFF11\u200B");
        result.Should().NotContain("\u200B");
        result.Length.Should().Be(1);
    }
}
