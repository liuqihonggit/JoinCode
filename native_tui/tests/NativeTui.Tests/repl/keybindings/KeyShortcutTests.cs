namespace NativeTui.Tests.Repl;

/// <summary>
/// 快捷键值类型测试 — 对齐基准 keybindings/parser.ts parseKeystroke + match.ts 匹配语义：
/// '+' 分段修饰归一（ctrl/control、alt/opt/option/meta）、键名表（escape/enter/tab/backtab/.../space/单字符）、
/// shift+tab→BackTab 归一、Char 小写比较 + Shift 全等、Escape 忽略 Meta quirk。
/// </summary>
public sealed class KeyShortcutTests
{
    /// <summary>解析为指定 Kind/Input/修饰（Theory 数据驱动全键名表）。</summary>
    [Theory]
    [InlineData("ctrl+x", KeyKind.Char, "x", true, false, false)]
    [InlineData("control+x", KeyKind.Char, "x", true, false, false)]
    [InlineData("shift+tab", KeyKind.BackTab, "", false, false, true)]
    [InlineData("backtab", KeyKind.BackTab, "", false, false, true)]
    [InlineData("escape", KeyKind.Escape, "", false, false, false)]
    [InlineData("esc", KeyKind.Escape, "", false, false, false)]
    [InlineData("enter", KeyKind.Return, "", false, false, false)]
    [InlineData("return", KeyKind.Return, "", false, false, false)]
    [InlineData("tab", KeyKind.Tab, "", false, false, false)]
    [InlineData("up", KeyKind.UpArrow, "", false, false, false)]
    [InlineData("↑", KeyKind.UpArrow, "", false, false, false)]
    [InlineData("down", KeyKind.DownArrow, "", false, false, false)]
    [InlineData("left", KeyKind.LeftArrow, "", false, false, false)]
    [InlineData("right", KeyKind.RightArrow, "", false, false, false)]
    [InlineData("home", KeyKind.Home, "", false, false, false)]
    [InlineData("end", KeyKind.End, "", false, false, false)]
    [InlineData("pageup", KeyKind.PageUp, "", false, false, false)]
    [InlineData("pagedown", KeyKind.PageDown, "", false, false, false)]
    [InlineData("backspace", KeyKind.Backspace, "", false, false, false)]
    [InlineData("delete", KeyKind.Delete, "", false, false, false)]
    [InlineData("alt+enter", KeyKind.Return, "", false, true, false)]
    [InlineData("meta+p", KeyKind.Char, "p", false, true, false)]
    [InlineData("opt+p", KeyKind.Char, "p", false, true, false)]
    [InlineData("option+p", KeyKind.Char, "p", false, true, false)]
    [InlineData("space", KeyKind.Char, " ", false, false, false)]
    [InlineData("ctrl+_", KeyKind.Char, "_", true, false, false)]
    [InlineData("ctrl+shift+-", KeyKind.Char, "-", true, false, true)]
    [InlineData("q", KeyKind.Char, "q", false, false, false)]
    [InlineData("/", KeyKind.Char, "/", false, false, false)]
    [InlineData("ctrl+shift+o", KeyKind.Char, "o", true, false, true)]
    public void Parse_ProducesExpectedShape(string text, KeyKind kind, string input, bool ctrl, bool meta, bool shift)
    {
        var shortcut = KeyShortcut.Parse(text);
        Assert.Equal(kind, shortcut.Kind);
        Assert.Equal(input, shortcut.Input);
        Assert.Equal(ctrl, shortcut.Ctrl);
        Assert.Equal(meta, shortcut.Meta);
        Assert.Equal(shift, shortcut.Shift);
    }

    /// <summary>ToString 反向与 Parse 往返一致（调试/错误提示规范形）。</summary>
    [Theory]
    [InlineData("ctrl+x")]
    [InlineData("shift+tab")]
    [InlineData("escape")]
    [InlineData("alt+enter")]
    [InlineData("space")]
    [InlineData("ctrl+shift+-")]
    [InlineData("ctrl+_")]
    [InlineData("up")]
    [InlineData("ctrl+home")]
    [InlineData("ctrl+shift+c")]
    public void ToString_RoundTripsThroughParse(string text)
    {
        var normalized = KeyShortcut.Parse(text).ToString();
        Assert.Equal(text, normalized);
        Assert.Equal(KeyShortcut.Parse(text), KeyShortcut.Parse(normalized));
    }

    /// <summary>Matches 精确命中：Kind + 三修饰全等（Char 大小写不敏感 — 'A' 命中 shift+a）。</summary>
    [Fact]
    public void Matches_HitsWithExactKindAndModifiers()
    {
        Assert.True(KeyShortcut.Parse("ctrl+x").Matches(new KeyPress(KeyKind.Char, "x", Ctrl: true)));
        Assert.True(KeyShortcut.Parse("shift+a").Matches(new KeyPress(KeyKind.Char, "A", Shift: true)));
        Assert.True(KeyShortcut.Parse("meta+p").Matches(new KeyPress(KeyKind.Char, "p", Meta: true)));
        Assert.True(KeyShortcut.Parse("space").Matches(new KeyPress(KeyKind.Char, " ")));
        Assert.True(KeyShortcut.Parse("escape").Matches(KeyPress.Named(KeyKind.Escape)));
        Assert.True(KeyShortcut.Parse("enter").Matches(KeyPress.Named(KeyKind.Return)));
        Assert.True(KeyShortcut.Parse("alt+enter").Matches(KeyPress.Named(KeyKind.Return, meta: true)));
    }

    /// <summary>shift+tab 命中 ESC[Z 产出（w2 KeyPressParser 把 BackTab 归一为恒带 Shift）。</summary>
    [Fact]
    public void Matches_ShiftTabHitsBackTabPressEvent()
    {
        var backTab = KeyPress.Named(KeyKind.BackTab, shift: true);
        Assert.True(KeyShortcut.Parse("shift+tab").Matches(backTab));
        Assert.True(KeyShortcut.Parse("backtab").Matches(backTab));
    }

    /// <summary>Escape 忽略 Meta（基准 match.ts:96-105 quirk — ESC ESC 第二键带 Meta 仍命中 escape 绑定）。</summary>
    [Fact]
    public void Matches_EscapeIgnoresMetaQuirk()
    {
        var escapeWithMeta = KeyPress.Named(KeyKind.Escape, meta: true);
        Assert.True(KeyShortcut.Parse("escape").Matches(escapeWithMeta));
    }

    /// <summary>修饰差异不命中（Ctrl/Shift/Meta 任一不等即拒绝）。</summary>
    [Fact]
    public void Matches_RejectsModifierMismatches()
    {
        Assert.False(KeyShortcut.Parse("ctrl+x").Matches(new KeyPress(KeyKind.Char, "x")));
        Assert.False(KeyShortcut.Parse("shift+a").Matches(new KeyPress(KeyKind.Char, "a")));
        Assert.False(KeyShortcut.Parse("meta+p").Matches(new KeyPress(KeyKind.Char, "p")));
        Assert.False(KeyShortcut.Parse("alt+enter").Matches(KeyPress.Named(KeyKind.Return)));
        Assert.False(KeyShortcut.Parse("ctrl+x").Matches(new KeyPress(KeyKind.Char, "X", Ctrl: true, Shift: true)));
    }

    /// <summary>Kind 差异不命中（Return ≠ Tab、Char 'x' ≠ escape 等）。</summary>
    [Fact]
    public void Matches_RejectsKindMismatches()
    {
        Assert.False(KeyShortcut.Parse("enter").Matches(KeyPress.Named(KeyKind.Tab)));
        Assert.False(KeyShortcut.Parse("escape").Matches(new KeyPress(KeyKind.Char, "e")));
        Assert.False(KeyShortcut.Parse("up").Matches(KeyPress.Named(KeyKind.DownArrow)));
    }

    /// <summary>解析失败收集四要素错误（哪个值/为什么/解析层拦截/正确写法示例）。</summary>
    [Theory]
    [InlineData("F13")]
    [InlineData("abc")]
    [InlineData("ctrl++")]
    [InlineData("ctrl")]
    [InlineData("wheelup")]
    [InlineData("paste")]
    [InlineData("")]
    [InlineData("ctrl+x+y")]
    public void TryParse_RejectsInvalidInputsWithGuidance(string text)
    {
        var ok = KeyShortcut.TryParse(text, out _, out var error);
        Assert.False(ok);
        Assert.Contains("KeyShortcut 解析层拦截", error);
        Assert.Contains("正确写法", error);
    }

    /// <summary>Super 修饰（cmd/command/super/win）显式拒绝并引导改用 meta/ctrl（w2 无 Super 通道）。</summary>
    [Theory]
    [InlineData("cmd+p")]
    [InlineData("command+p")]
    [InlineData("super+p")]
    [InlineData("win+p")]
    public void TryParse_RejectsSuperModifierWithGuidance(string text)
    {
        var ok = KeyShortcut.TryParse(text, out _, out var error);
        Assert.False(ok);
        Assert.Contains("不受支持", error);
        Assert.Contains("meta", error);
    }
}
