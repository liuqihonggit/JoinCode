namespace NativeTui.Tests.Terminal;

/// <summary>
/// 终端查询响应解析测试 — 基准 ink/parse-keypress.ts:33-60（模式定义）+ :96-175（parseTerminalResponse）：
/// 程序主动发的终端探测查询（DECRQM/DA1/DA2/kitty flags/DSR/XTVERSION），回复经 stdin 回流
/// 被识别为 TerminalResponse（非键事件）— 探测链路地基（kitty 协议/能力探测的前置）。
/// </summary>
public sealed class TerminalResponseParserTests
{
    /// <summary>DECRPM 回复（ESC[?mode;value$y — DECRQM 查询的应答）。</summary>
    [Fact]
    public void DecrpmReply_ParsesModeAndValue()
    {
        var response = TerminalResponseParser.TryParse("\u001b[?2026;2$y", out var consumed);
        Assert.NotNull(response);
        Assert.Equal(TerminalResponseKind.Decrpm, response.Kind);
        Assert.Equal([2026, 2], response.Numbers);
        Assert.Equal(11, consumed);
    }

    /// <summary>DA1 回复（ESC[?a;b c — 主设备属性）。</summary>
    [Fact]
    public void Da1Reply_Parses()
    {
        var response = TerminalResponseParser.TryParse("\u001b[?1;2c", out var consumed);
        Assert.NotNull(response);
        Assert.Equal(TerminalResponseKind.Da1, response.Kind);
        Assert.Equal([1, 2], response.Numbers);
        Assert.Equal(7, consumed);
    }

    /// <summary>DA2 回复（ESC[>a;b;c c — 次设备属性，VT520+/xterm）。</summary>
    [Fact]
    public void Da2Reply_Parses()
    {
        var response = TerminalResponseParser.TryParse("\u001b[>0;276;0c", out var consumed);
        Assert.NotNull(response);
        Assert.Equal(TerminalResponseKind.Da2, response.Kind);
        Assert.Equal([0, 276, 0], response.Numbers);
        Assert.Equal(11, consumed);
    }

    /// <summary>kitty flags 回复（ESC[?flags u — CSI ? u 查询的应答）。</summary>
    [Fact]
    public void KittyFlagsReply_Parses()
    {
        var response = TerminalResponseParser.TryParse("\u001b[?15u", out var consumed);
        Assert.NotNull(response);
        Assert.Equal(TerminalResponseKind.KittyFlags, response.Kind);
        Assert.Equal([15], response.Numbers);
        Assert.Equal(6, consumed);
    }

    /// <summary>DSR 光标位置回复（ESC[row;col R — CSI 6 n 查询的应答）。</summary>
    [Fact]
    public void CursorPositionReply_Parses()
    {
        var response = TerminalResponseParser.TryParse("\u001b[12;40R", out var consumed);
        Assert.NotNull(response);
        Assert.Equal(TerminalResponseKind.CursorPosition, response.Kind);
        Assert.Equal([12, 40], response.Numbers);
        Assert.Equal(8, consumed);
    }

    /// <summary>XTVERSION 回复（OSC 0 厂商名 BEL 终结）。</summary>
    [Fact]
    public void XtVersionReply_ParsesPayload()
    {
        var response = TerminalResponseParser.TryParse("\u001b]0;xterm(370)\u0007", out var consumed);
        Assert.NotNull(response);
        Assert.Equal(TerminalResponseKind.XtVersion, response.Kind);
        Assert.Equal("xterm(370)", response.Payload);
        Assert.Equal(15, consumed);
    }

    /// <summary>OSC 通用回复（ST 终结变体 — ESC \ ）。</summary>
    [Fact]
    public void OscReply_StTerminator_ParsesPayload()
    {
        var response = TerminalResponseParser.TryParse("\u001b]11;rgb:1/1/1\u001b\\", out var consumed);
        Assert.NotNull(response);
        Assert.Equal(TerminalResponseKind.Osc, response.Kind);
        Assert.Equal("11;rgb:1/1/1", response.Payload);
        Assert.Equal(16, consumed);
    }

    /// <summary>非回复序列（普通键 CSI）— 返回 null 不误吞（键解析继续）。</summary>
    [Theory]
    [InlineData("\u001b[A")]
    [InlineData("\u001b[1;5D")]
    [InlineData("\u001b[200~pasted")]
    [InlineData("abc")]
    public void NonReplyInput_ReturnsNull(string chunk)
    {
        Assert.Null(TerminalResponseParser.TryParse(chunk, out _));
    }

    /// <summary>不完整回复前缀（跨 chunk 攒齐语义交由调用方）— 返回 null 不误判。</summary>
    [Fact]
    public void IncompleteReplyPrefix_ReturnsNull()
    {
        Assert.Null(TerminalResponseParser.TryParse("\u001b[?2026", out _));
    }
}
