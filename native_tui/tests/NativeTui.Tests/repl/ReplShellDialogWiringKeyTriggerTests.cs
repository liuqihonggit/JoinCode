namespace NativeTui.Tests.Repl;

/// <summary>
/// S12 四域对话框统一接线 — ctrl+shift+p/f 触发键位归一双通道冒烟：
/// ReadKey 路径（ConsoleKeyAdapter 控制字母分支保留 Shift 位 — ctrl+shift+字母 KeyChar
/// 与 ctrl+字母同码，Shift 只在 Mods）；VT 路径（KeyPressParser kitty keyboard protocol
/// CSI codepoint;modifiers u 终点符 — Windows Terminal 等 kitty 协议终端 ctrl+shift+字母
/// 唯一可区分形态）；既有 CSI ;6 方向键位掩码与 ctrl+字母无 Shift 语义回归。
/// </summary>
public sealed class ReplShellDialogWiringKeyTriggerTests
{
    /// <summary>解析单个完整序列（断言一次成功 — 多余序列为测试缺陷）。</summary>
    private static KeyPress ParseOne(string chunk)
    {
        var buffer = chunk.AsSpan();
        Assert.True(KeyPressParser.TryParseNext(ref buffer, out var key));
        return key;
    }

    /// <summary>ReadKey 路径 — ctrl+shift+p：控制字母分支保留 Shift 位（Char "p" + Ctrl + Shift）。</summary>
    [Fact]
    public void ConsoleAdapter_CtrlShiftP_KeepsShift()
    {
        var key = ConsoleKeyAdapter.ToKeyPress(new ConsoleKeyInfo('\x10', ConsoleKey.P, shift: true, alt: false, control: true));

        Assert.NotNull(key);
        Assert.Equal(KeyKind.Char, key!.Kind);
        Assert.Equal("p", key.Input);
        Assert.True(key.Ctrl);
        Assert.True(key.Shift);
        Assert.False(key.Meta);
    }

    /// <summary>ReadKey 路径 — ctrl+shift+f：同上归一（Char "f" + Ctrl + Shift）。</summary>
    [Fact]
    public void ConsoleAdapter_CtrlShiftF_KeepsShift()
    {
        var key = ConsoleKeyAdapter.ToKeyPress(new ConsoleKeyInfo('\x06', ConsoleKey.F, shift: true, alt: false, control: true));

        Assert.NotNull(key);
        Assert.Equal(KeyKind.Char, key!.Kind);
        Assert.Equal("f", key.Input);
        Assert.True(key.Ctrl);
        Assert.True(key.Shift);
    }

    /// <summary>ReadKey 路径回归 — 裸 ctrl+p：无 Shift（既有编辑/滚动语义不受保留位影响）。</summary>
    [Fact]
    public void ConsoleAdapter_CtrlP_Alone_NoShift()
    {
        var key = ConsoleKeyAdapter.ToKeyPress(new ConsoleKeyInfo('\x10', ConsoleKey.P, shift: false, alt: false, control: true));

        Assert.NotNull(key);
        Assert.Equal("p", key!.Input);
        Assert.True(key.Ctrl);
        Assert.False(key.Shift);
    }

    /// <summary>VT 路径 — kitty CSI 112;6u（ctrl+shift+p）：Char "p" + Shift + Ctrl。</summary>
    [Fact]
    public void KittySequence_CtrlShiftP_ParsesWithShiftCtrl()
    {
        var key = ParseOne("\u001b[112;6u");

        Assert.Equal(KeyKind.Char, key.Kind);
        Assert.Equal("p", key.Input);
        Assert.True(key.Ctrl);
        Assert.True(key.Shift);
        Assert.False(key.Meta);
    }

    /// <summary>VT 路径 — kitty CSI 102;6u（ctrl+shift+f）：Char "f" + Shift + Ctrl。</summary>
    [Fact]
    public void KittySequence_CtrlShiftF_ParsesWithShiftCtrl()
    {
        var key = ParseOne("\u001b[102;6u");

        Assert.Equal(KeyKind.Char, key.Kind);
        Assert.Equal("f", key.Input);
        Assert.True(key.Ctrl);
        Assert.True(key.Shift);
    }

    /// <summary>VT 路径 — kitty 无修饰 CSI 97u（'a'）：纯字符无修饰。</summary>
    [Fact]
    public void KittySequence_BareCodepoint_ParsesAsChar()
    {
        var key = ParseOne("\u001b[97u");

        Assert.Equal(KeyKind.Char, key.Kind);
        Assert.Equal("a", key.Input);
        Assert.False(key.Ctrl);
        Assert.False(key.Shift);
    }

    /// <summary>VT 路径回归 — CSI ;6 方向键位掩码（bits=5 → Shift+Ctrl）既有语义。</summary>
    [Fact]
    public void CsiModifier_6_AppliesShiftAndCtrl()
    {
        var key = ParseOne("\u001b[1;6A");

        Assert.Equal(KeyKind.UpArrow, key.Kind);
        Assert.True(key.Shift);
        Assert.True(key.Ctrl);
        Assert.False(key.Meta);
    }
}
