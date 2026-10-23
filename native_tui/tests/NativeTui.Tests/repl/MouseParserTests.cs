namespace NativeTui.Tests.Repl;

/// <summary>
/// SGR 鼠标解码测试（DECSET 1006）— 对齐基准 ink/parse-keypress.ts:
/// :594-609 parseMouseEvent（press/release + 坐标）、:683-684 滚轮 0x43 掩码判定
/// （:679-680 修饰滚轮 button=80 例）、:573-575 位序注释（bit0-1 按钮 / bit2 Shift /
/// bit3 Meta / bit4 Ctrl / bit5 motion / bit6 wheel）+ events/click-event.ts:11-12
/// 0-based 坐标消费语义。
/// 覆盖：三键按下释放、滚轮四方向（含修饰滚轮）、修饰位、拖拽/纯移动、
/// 不完整前缀 false 不消耗、非法形态整体吞 Unknown 防泄漏、混合 chunk 与普通按键共存。
/// </summary>
public sealed class MouseParserTests
{
    /// <summary>循环解析整个 chunk 为按键列表。</summary>
    private static List<KeyPress> ParseAll(string chunk)
    {
        var span = chunk.AsSpan();
        var keys = new List<KeyPress>();
        while (KeyPressParser.TryParseNext(ref span, out var key))
        {
            keys.Add(key);
        }

        return keys;
    }

    /// <summary>解析 chunk 的第一个按键。</summary>
    private static KeyPress ParseOne(string chunk) => ParseAll(chunk)[0];

    /// <summary>断言不完整前缀：返回 false 且 buffer 长度不变（不消耗，等待更多输入攒齐）。</summary>
    private static void AssertIncomplete(string chunk)
    {
        var span = chunk.AsSpan();
        Assert.False(KeyPressParser.TryParseNext(ref span, out _));
        Assert.Equal(chunk.Length, span.Length);
    }

    [Fact]
    public void LeftPress_DecodesToZeroBasedMouse()
    {
        var key = ParseOne("\x1b[<0;10;5M");
        Assert.Equal(KeyKind.Mouse, key.Kind);
        Assert.Equal(string.Empty, key.Input);
        var mouse = key.Mouse!.Value;
        Assert.Equal(MouseKind.Press, mouse.Kind);
        Assert.Equal(MouseButton.Left, mouse.Button);
        Assert.Equal(9, mouse.Col);
        Assert.Equal(4, mouse.Row);
        Assert.False(mouse.Shift);
        Assert.False(mouse.Meta);
        Assert.False(mouse.Ctrl);
    }

    [Fact]
    public void LeftRelease_ParsesAsRelease()
    {
        var mouse = ParseOne("\x1b[<0;10;5m").Mouse!.Value;
        Assert.Equal(MouseKind.Release, mouse.Kind);
        Assert.Equal(MouseButton.Left, mouse.Button);
        Assert.Equal(9, mouse.Col);
        Assert.Equal(4, mouse.Row);
    }

    [Theory]
    [InlineData("\x1b[<0;10;5M", MouseButton.Left)]
    [InlineData("\x1b[<1;10;5M", MouseButton.Middle)]
    [InlineData("\x1b[<2;10;5M", MouseButton.Right)]
    public void ButtonBits_MapToButtons(string chunk, MouseButton expected)
    {
        var mouse = ParseOne(chunk).Mouse!.Value;
        Assert.Equal(MouseKind.Press, mouse.Kind);
        Assert.Equal(expected, mouse.Button);
        Assert.Equal(9, mouse.Col);
        Assert.Equal(4, mouse.Row);
    }

    [Theory]
    [InlineData(64, MouseKind.WheelUp)]
    [InlineData(65, MouseKind.WheelDown)]
    [InlineData(66, MouseKind.WheelLeft)]
    [InlineData(67, MouseKind.WheelRight)]
    [InlineData(68, MouseKind.WheelUp)]
    public void WheelCodes_ParseByDirectionMask(int b, MouseKind expected)
    {
        var mouse = ParseOne($"\x1b[<{b};10;5M").Mouse!.Value;
        Assert.Equal(expected, mouse.Kind);
        Assert.Equal(9, mouse.Col);
        Assert.Equal(4, mouse.Row);
    }

    [Fact]
    public void CtrlModifiedWheel_Button80_IsWheelUpWithCtrl()
    {
        // 基准 parse-keypress.ts:679-680 — Ctrl+scroll（button=80=64+16）仍识别为 wheelup
        var mouse = ParseOne("\x1b[<80;10;5M").Mouse!.Value;
        Assert.Equal(MouseKind.WheelUp, mouse.Kind);
        Assert.True(mouse.Ctrl);
        Assert.False(mouse.Shift);
        Assert.False(mouse.Meta);
    }

    [Theory]
    [InlineData(4, true, false, false)]
    [InlineData(8, false, true, false)]
    [InlineData(16, false, false, true)]
    [InlineData(28, true, true, true)]
    public void ModifierBits_DecodeToFlags(int b, bool shift, bool meta, bool ctrl)
    {
        var mouse = ParseOne($"\x1b[<{b};10;5M").Mouse!.Value;
        Assert.Equal(MouseKind.Press, mouse.Kind);
        Assert.Equal(MouseButton.Left, mouse.Button);
        Assert.Equal(shift, mouse.Shift);
        Assert.Equal(meta, mouse.Meta);
        Assert.Equal(ctrl, mouse.Ctrl);
    }

    [Fact]
    public void MotionWithButton_ParsesAsDrag()
    {
        var mouse = ParseOne("\x1b[<32;10;5M").Mouse!.Value;
        Assert.Equal(MouseKind.Drag, mouse.Kind);
        Assert.Equal(MouseButton.Left, mouse.Button);
        Assert.Equal(9, mouse.Col);
        Assert.Equal(4, mouse.Row);
    }

    [Fact]
    public void ShiftDrag_KeepsModifier()
    {
        // b=36 = motion(32) + shift(4) + 左键(0)
        var mouse = ParseOne("\x1b[<36;10;5M").Mouse!.Value;
        Assert.Equal(MouseKind.Drag, mouse.Kind);
        Assert.True(mouse.Shift);
    }

    [Fact]
    public void MotionWithoutButton_ParsesAsMove()
    {
        // b=35 = motion(32) + 无按钮(3) — 纯 hover 移动
        var mouse = ParseOne("\x1b[<35;10;5M").Mouse!.Value;
        Assert.Equal(MouseKind.Move, mouse.Kind);
    }

    [Fact]
    public void OrdinaryKey_MousePropertyIsNull()
    {
        Assert.Null(ParseOne("a").Mouse);
        Assert.Null(ParseOne("\x1b[A").Mouse);
    }

    [Theory]
    [InlineData("\x1b[<")]
    [InlineData("\x1b[<0")]
    [InlineData("\x1b[<0;10")]
    [InlineData("\x1b[<0;10;")]
    public void IncompletePrefix_ReturnsFalseWithoutConsuming(string chunk)
    {
        AssertIncomplete(chunk);
    }

    [Theory]
    [InlineData("\x1b[<0;10Mabc")]   // 分号数≠2 — final 'M' 一并吞掉不泄漏
    [InlineData("\x1b[<0;;5Mabc")]   // 空段 — 非法吞 Unknown
    [InlineData("\x1b[<0;0;5Mabc")]  // col=0 畸形（SGR 坐标 1-based 最小 1）
    public void MalformedSequence_SwallowedAsUnknownThenTextParses(string chunk)
    {
        var keys = ParseAll(chunk);
        Assert.Equal(4, keys.Count);
        Assert.Equal(KeyKind.Unknown, keys[0].Kind);
        Assert.Equal(string.Empty, keys[0].Input);
        Assert.Equal(KeyKind.Char, keys[1].Kind);
        Assert.Equal("a", keys[1].Input);
        Assert.Equal("b", keys[2].Input);
        Assert.Equal("c", keys[3].Input);
    }

    [Fact]
    public void MixedChunk_MouseThenChar()
    {
        var keys = ParseAll("\x1b[<0;10;5Ma");
        Assert.Equal(2, keys.Count);
        Assert.Equal(KeyKind.Mouse, keys[0].Kind);
        Assert.Equal(KeyKind.Char, keys[1].Kind);
        Assert.Equal("a", keys[1].Input);
    }

    [Fact]
    public void MixedChunk_CharWheelReturn()
    {
        var keys = ParseAll("a\x1b[<64;3;2M\r");
        Assert.Equal(3, keys.Count);
        Assert.Equal(KeyKind.Char, keys[0].Kind);
        Assert.Equal(KeyKind.Mouse, keys[1].Kind);
        Assert.Equal(MouseKind.WheelUp, keys[1].Mouse!.Value.Kind);
        Assert.Equal(2, keys[1].Mouse!.Value.Col);
        Assert.Equal(1, keys[1].Mouse!.Value.Row);
        Assert.Equal(KeyKind.Return, keys[2].Kind);
    }
}
