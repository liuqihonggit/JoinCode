// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003

namespace Abs.Tests.Utils;

/// <summary>
/// AnsiStripper 单元测试 — 验证 CSI/OSC/单字节转义清洗、零分配快路径、边界条件。
/// <para>GAP-038-01: GitHub Actions 彩色日志污染 LLM 上下文</para>
/// </summary>
public sealed class AnsiStripperTests {
    private const char Esc = '\x1b';

    [Fact]
    public void Strip_Empty_ReturnsEmpty() {
        Assert.Equal(string.Empty, AnsiStripper.Strip(string.Empty));
    }

    [Fact]
    public void Strip_Null_ReturnsNull() {
        Assert.Null(AnsiStripper.Strip(null!));
    }

    [Fact]
    public void Strip_NoAnsi_ReturnsSameReference() {
        var input = "plain text no ansi here";
        var result = AnsiStripper.Strip(input);
        Assert.Same(input, result);
    }

    [Fact]
    public void Strip_SingleSgr_PreservesText() {
        var input = $"{Esc}[31mred{Esc}[0m";
        Assert.Equal("red", AnsiStripper.Strip(input));
    }

    [Fact]
    public void Strip_MultiParamSgr_PreservesText() {
        var input = $"{Esc}[1;31mbold red{Esc}[0m";
        Assert.Equal("bold red", AnsiStripper.Strip(input));
    }

    [Fact]
    public void Strip_256Color_PreservesText() {
        var input = $"{Esc}[38;5;196mtext{Esc}[0m";
        Assert.Equal("text", AnsiStripper.Strip(input));
    }

    [Fact]
    public void Strip_TrueColor_PreservesText() {
        var input = $"{Esc}[38;2;255;0;0mtext{Esc}[0m";
        Assert.Equal("text", AnsiStripper.Strip(input));
    }

    [Fact]
    public void Strip_CursorMovement_PreservesText() {
        var input = $"{Esc}[2J{Esc}[Htext";
        Assert.Equal("text", AnsiStripper.Strip(input));
    }

    [Fact]
    public void Strip_EraseLine_PreservesText() {
        var input = $"before{Esc}[2Kafter";
        Assert.Equal("beforeafter", AnsiStripper.Strip(input));
    }

    [Fact]
    public void Strip_OscWithBel_PreservesText() {
        var input = $"{Esc}]0;window title\u0007text";
        Assert.Equal("text", AnsiStripper.Strip(input));
    }

    [Fact]
    public void Strip_OscWithSt_PreservesText() {
        var input = $"{Esc}]0;title{Esc}\\text";
        Assert.Equal("text", AnsiStripper.Strip(input));
    }

    [Fact]
    public void Strip_SingleByteEscape_PreservesText() {
        var input = $"{Esc}ctext";
        Assert.Equal("text", AnsiStripper.Strip(input));
    }

    [Fact]
    public void Strip_MixedAnsiAndText_PreservesText() {
        var input = $"a{Esc}[31mb{Esc}[0mc{Esc}]0;t\u0007d";
        Assert.Equal("abcd", AnsiStripper.Strip(input));
    }

    [Fact]
    public void Strip_OnlyAnsi_ReturnsEmpty() {
        var input = $"{Esc}[31m{Esc}[0m{Esc}[2J";
        Assert.Equal(string.Empty, AnsiStripper.Strip(input));
    }

    [Fact]
    public void Strip_OrphanEsc_Swallows() {
        Assert.Equal(string.Empty, AnsiStripper.Strip(Esc.ToString()));
    }

    [Fact]
    public void Strip_TruncatedCsi_SwallowsGracefully() {
        var input = $"text{Esc}[31";
        Assert.Equal("text", AnsiStripper.Strip(input));
    }

    [Fact]
    public void Strip_TruncatedOsc_SwallowsGracefully() {
        var input = $"text{Esc}]0;no terminator";
        Assert.Equal("text", AnsiStripper.Strip(input));
    }

    [Fact]
    public void Strip_DotnetTestOutput_PreservesText() {
        var input = $"{Esc}[32m  Passed! {Esc}[0m - 3 tests in 1.2s";
        Assert.Equal("  Passed!  - 3 tests in 1.2s", AnsiStripper.Strip(input));
    }

    [Fact]
    public void Strip_NpmOutput_PreservesText() {
        var input = $"{Esc}[34m{Esc}[1m[1/4] Resolving dependencies...{Esc}[0m{Esc}[0m";
        Assert.Equal("[1/4] Resolving dependencies...", AnsiStripper.Strip(input));
    }

    [Fact]
    public void Strip_MultipleLines_PreservesAllText() {
        var input = $"line1{Esc}[31m{Esc}[0m\nline2{Esc}[32m{Esc}[0m\nline3";
        Assert.Equal("line1\nline2\nline3", AnsiStripper.Strip(input));
    }

    [Fact]
    public void Strip_LargeInput_StackAllocPath() {
        var text = new string('a', 2048);
        var input = $"{Esc}[31m{text}{Esc}[0m";
        Assert.Equal(text, AnsiStripper.Strip(input));
    }

    [Fact]
    public void Strip_LargeInput_HeapAllocPath() {
        var text = new string('b', 8192);
        var input = $"{Esc}[31m{text}{Esc}[0m";
        Assert.Equal(text, AnsiStripper.Strip(input));
    }

    [Fact]
    public void StripSpan_NoAnsi_CopiesAll() {
        var input = "plain text".AsSpan();
        Span<char> output = stackalloc char[input.Length];
        var written = AnsiStripper.Strip(input, output);
        Assert.Equal(input.Length, written);
        Assert.Equal("plain text", new string(output[..written]));
    }

    [Fact]
    public void StripSpan_WithAnsi_StripsCorrectly() {
        var input = $"{Esc}[31mhi{Esc}[0m".AsSpan();
        Span<char> output = stackalloc char[input.Length];
        var written = AnsiStripper.Strip(input, output);
        Assert.Equal(2, written);
        Assert.Equal("hi", new string(output[..written]));
    }

    [Fact]
    public void ContainsAnsi_DetectsCorrectly() {
        Assert.True(AnsiStripper.ContainsAnsi($"{Esc}[31mtext".AsSpan()));
        Assert.False(AnsiStripper.ContainsAnsi("plain text".AsSpan()));
        Assert.False(AnsiStripper.ContainsAnsi(ReadOnlySpan<char>.Empty));
    }

    [Fact]
    public void Strip_RealWorldGitHubActionsLog_PreservesContent() {
        var input = $"{Esc}[36m2026-10-09T12:00:00.0000000Z {Esc}[0m{Esc}[32m  Passed! {Esc}[0m - {Esc}[1mMyProject.Tests{Esc}[0m (net10.0)\n{Esc}[31m  Failed! {Esc}[0m - {Esc}[1mOther.Tests{Esc}[0m (net10.0)";
        var expected = "2026-10-09T12:00:00.0000000Z   Passed!  - MyProject.Tests (net10.0)\n  Failed!  - Other.Tests (net10.0)";
        Assert.Equal(expected, AnsiStripper.Strip(input));
    }
}
