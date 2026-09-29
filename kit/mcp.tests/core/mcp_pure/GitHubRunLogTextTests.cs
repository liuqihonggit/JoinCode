namespace Mcp.Tests;

/// <summary>
/// GitHubRunLogText 单元测试 — 验证日志文本处理(去时间戳 + 去 ANSI 转义码)
/// </summary>
public sealed class GitHubRunLogTextTests {
    [Fact]
    public void StripLogTimestamp_ZPrefix_StripsTimestamp() {
        var line = "2026-09-07T17:08:27.5016453Z content here";
        GitHubRunLogText.StripLogTimestamp(line).Should().Be("content here");
    }

    [Fact]
    public void StripLogTimestamp_ZPrefix_WithAnsi_StripsBoth() {
        var line = "2026-09-07T17:08:27.5016453Z \x1B[36;1mcontent\x1B[0m";
        GitHubRunLogText.StripLogTimestamp(line).Should().Be("content");
    }

    [Fact]
    public void StripLogTimestamp_BracketPrefix_StripsEntryName() {
        var line = "[0_Checkout.txt] building...";
        GitHubRunLogText.StripLogTimestamp(line).Should().Be("building...");
    }

    [Fact]
    public void StripLogTimestamp_NoPrefix_ReturnsStrippedAnsi() {
        var line = "plain text";
        GitHubRunLogText.StripLogTimestamp(line).Should().Be("plain text");
    }

    [Fact]
    public void StripLogTimestamp_NoPrefix_WithAnsi_StripsAnsi() {
        var line = "\x1B[32msuccess\x1B[0m";
        GitHubRunLogText.StripLogTimestamp(line).Should().Be("success");
    }

    [Fact]
    public void StripAnsiEscapes_NoEscapes_ReturnsAsIs() {
        GitHubRunLogText.StripAnsiEscapes("hello world").Should().Be("hello world");
    }

    [Fact]
    public void StripAnsiEscapes_SingleColorSequence_Removed() {
        GitHubRunLogText.StripAnsiEscapes("\x1B[36;1mtext\x1B[0m").Should().Be("text");
    }

    [Fact]
    public void StripAnsiEscapes_MultipleSequences_Removed() {
        GitHubRunLogText.StripAnsiEscapes("\x1B[31mred\x1B[0m and \x1B[32mgreen\x1B[0m").Should().Be("red and green");
    }

    [Fact]
    public void StripAnsiEscapes_EscAtEnd_Ignored() {
        // ESC 在末尾,不构成序列,保留(无 [ 跟随)
        var result = GitHubRunLogText.StripAnsiEscapes("text\x1B");
        result.Should().Be("text\x1B");
    }

    [Fact]
    public void StripAnsiEscapes_EmptyString_ReturnsEmpty() {
        GitHubRunLogText.StripAnsiEscapes(string.Empty).Should().BeEmpty();
    }

    [Fact]
    public void StripAnsiEscapes_OnlyEscapes_ReturnsEmpty() {
        GitHubRunLogText.StripAnsiEscapes("\x1B[1m\x1B[0m").Should().BeEmpty();
    }

    [Fact]
    public void StripAnsiEscapes_NestedSequences_Removed() {
        // 多个 ESC 序列混合普通文本
        GitHubRunLogText.StripAnsiEscapes("a\x1B[1mb\x1B[0mc\x1B[31md\x1B[0me").Should().Be("abcde");
    }
}
