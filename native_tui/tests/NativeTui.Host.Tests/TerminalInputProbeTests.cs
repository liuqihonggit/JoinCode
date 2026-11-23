namespace NativeTui.Host.Tests;

/// <summary>诊断输出保留按键证据，但不会重放终端控制字符或剪贴板正文。</summary>
public sealed class TerminalInputProbeTests {
    /// <summary>中文字符和 Ctrl 修饰能从输出区分。</summary>
    [Theory]
    [InlineData("你", false, "U+4F60")]
    [InlineData("q", true, "U+0071")]
    public void DescribeKey_ReportsCharacterCodePointAndModifiers(string input, bool ctrl, string expectedScalar) {
        var result = TerminalInputProbe.DescribeKey(new KeyPress(KeyKind.Char, input, Ctrl: ctrl));

        Assert.Contains(expectedScalar, result);
        Assert.Contains($"ctrl={ctrl}", result);
    }

    /// <summary>粘贴事件只输出长度，不输出载荷或控制序列。</summary>
    [Fact]
    public void DescribeKey_RedactsPasteContent() {
        const string paste = "中文\n\x1b[2J";
        var result = TerminalInputProbe.DescribeKey(new KeyPress(KeyKind.Paste, paste));

        Assert.Contains("Paste", result);
        Assert.Contains($"chars={paste.Length}", result);
        Assert.DoesNotContain("中文", result);
        Assert.DoesNotContain("\x1b", result);
    }

    /// <summary>鼠标修饰取自结构化鼠标事件，而非外层按键标记。</summary>
    [Fact]
    public void DescribeKey_UsesMousePayloadModifiers() {
        var mouse = new MouseEvent(MouseKind.WheelUp, MouseButton.Left, 8, 5, Shift: true, Meta: false, Ctrl: true);
        var result = TerminalInputProbe.DescribeKey(new KeyPress(KeyKind.Mouse, "", Mouse: mouse));

        Assert.Contains("WheelUp", result);
        Assert.Contains("col=8 row=5", result);
        Assert.Contains("ctrl=True alt=False shift=True", result);
    }
}
