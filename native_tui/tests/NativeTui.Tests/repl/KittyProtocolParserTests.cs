namespace NativeTui.Tests.Repl;

/// <summary>
/// kitty keyboard protocol（CSI u）与 xterm modifyOtherKeys 解析测试 —
/// 对齐基准 ink/parse-keypress.ts 的 CSI_U_RE / MODIFY_OTHER_KEYS_RE 分支：
/// 码点十进制（97='a'，13=Enter，27=Esc，127=Backspace，9=Tab）+ mod-1 位掩码修饰，
/// 双修饰组合（ctrl+shift）完整保留（kitty 协议核心价值 — 传统 \x01 控制码路径丢失 shift）。
/// 契约：不完整前缀返回 false 不消耗；非法段吞为 Unknown 防泄漏。
/// </summary>
public sealed class KittyProtocolParserTests
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

    [Fact]
    public void KittyBareCodepoint_ParsesAsChar()
    {
        // 基准 CSI_U_RE：ESC[97u = 'a'（无修饰段，mod 缺省 1）
        var key = ParseOne("\u001b[97u");
        Assert.Equal(KeyKind.Char, key.Kind);
        Assert.Equal("a", key.Input);
        Assert.False(key.Ctrl);
        Assert.False(key.Meta);
        Assert.False(key.Shift);
    }

    [Theory]
    [InlineData(13, KeyKind.Return)]
    [InlineData(27, KeyKind.Escape)]
    [InlineData(127, KeyKind.Backspace)]
    [InlineData(9, KeyKind.Tab)]
    public void KittyControlCodepoint_ParsesAsNamedKey(int codepoint, KeyKind expected)
    {
        var key = ParseOne($"\u001b[{codepoint}u");
        Assert.Equal(expected, key.Kind);
        Assert.Equal(string.Empty, key.Input);
        Assert.False(key.Ctrl);
        Assert.False(key.Meta);
        Assert.False(key.Shift);
    }

    [Fact]
    public void KittyCtrlModifier_SetsCtrl()
    {
        // ESC[97;5u = ctrl+a（mod 5 = 1+4 → bit2=Ctrl）
        var key = ParseOne("\u001b[97;5u");
        Assert.Equal(KeyKind.Char, key.Kind);
        Assert.Equal("a", key.Input);
        Assert.True(key.Ctrl);
        Assert.False(key.Meta);
        Assert.False(key.Shift);
    }

    [Fact]
    public void KittyAltModifier_SetsMeta()
    {
        // mod 3 = 1+2 → bit1=Alt
        var key = ParseOne("\u001b[97;3u");
        Assert.Equal(KeyKind.Char, key.Kind);
        Assert.Equal("a", key.Input);
        Assert.True(key.Meta);
        Assert.False(key.Ctrl);
        Assert.False(key.Shift);
    }

    [Fact]
    public void KittyShiftModifier_SetsShift()
    {
        // mod 2 = 1+1 → bit0=Shift
        var key = ParseOne("\u001b[97;2u");
        Assert.Equal(KeyKind.Char, key.Kind);
        Assert.Equal("a", key.Input);
        Assert.True(key.Shift);
        Assert.False(key.Ctrl);
        Assert.False(key.Meta);
    }

    [Fact]
    public void KittyCtrlShiftCombo_PreservesBothModifiers()
    {
        // kitty 协议核心价值：双修饰组合完整保留（传统 \x01 控制码路径丢失 shift）
        // mod 6 = 1+4+1 → bit0=Shift + bit2=Ctrl
        var key = ParseOne("\u001b[97;6u");
        Assert.Equal(KeyKind.Char, key.Kind);
        Assert.Equal("a", key.Input);
        Assert.True(key.Ctrl);
        Assert.True(key.Shift);
        Assert.False(key.Meta);
    }

    [Fact]
    public void KittyShiftEnter_ParsesAsShiftedReturn()
    {
        // 基准 CSI_U_RE 注释核心例：ESC[13;2u = Shift+Enter
        var key = ParseOne("\u001b[13;2u");
        Assert.Equal(KeyKind.Return, key.Kind);
        Assert.True(key.Shift);
        Assert.False(key.Ctrl);
        Assert.False(key.Meta);
    }

    [Fact]
    public void KittyShiftTab_ParsesAsTabWithShift()
    {
        var key = ParseOne("\u001b[9;2u");
        Assert.Equal(KeyKind.Tab, key.Kind);
        Assert.True(key.Shift);
        Assert.False(key.Ctrl);
    }

    [Fact]
    public void KittyUppercaseCodepoint_AutoSetsShift()
    {
        // 码点 65='A' — 复用 KeyPress.Char 构造的大写自动 Shift 语义
        var key = ParseOne("\u001b[65u");
        Assert.Equal(KeyKind.Char, key.Kind);
        Assert.Equal("A", key.Input);
        Assert.True(key.Shift);
        Assert.False(key.Ctrl);
    }

    [Fact]
    public void KittyTextCodepointSegment_Ignored()
    {
        // 第三段 text-codepoints 仅用于终端回显文本（如 shift 产生的字形）— 不消费语义
        // ESC[97;5;120u = ctrl+a（120='x' 回显文本被忽略）
        var key = ParseOne("\u001b[97;5;120u");
        Assert.Equal(KeyKind.Char, key.Kind);
        Assert.Equal("a", key.Input);
        Assert.True(key.Ctrl);
        Assert.False(key.Shift);
        Assert.False(key.Meta);
    }

    [Fact]
    public void KittyAstralCodepoint_ParsesAsChar()
    {
        // 星面码点 128512（U+1F600 😀）经 ConvertFromUtf32 产出代理对字符串，不误置 Shift
        var key = ParseOne("\u001b[128512u");
        Assert.Equal(KeyKind.Char, key.Kind);
        Assert.Equal("\U0001F600", key.Input);
        Assert.False(key.Shift);
    }

    [Theory]
    [InlineData(57414, KeyKind.F1)]
    [InlineData(57415, KeyKind.F2)]
    [InlineData(57416, KeyKind.F3)]
    [InlineData(57417, KeyKind.F4)]
    public void KittyFunctionKeyCodepoints_MapToFKeys(int codepoint, KeyKind expected)
    {
        // kitty 功能键区 57414-57417（F1-F4）— KeyKind.F1-F12 引入（B2 键位系统）后精确映射
        var key = ParseOne($"\u001b[{codepoint}u");
        Assert.Equal(expected, key.Kind);
        Assert.Equal(string.Empty, key.Input);
    }

    [Fact]
    public void ModifyOtherKeys_CtrlLetter()
    {
        // 基准 MODIFY_OTHER_KEYS_RE：ESC[27;5;97~ = ctrl+a（参数顺序与 CSI u 相反：修饰前、键码后）
        var key = ParseOne("\u001b[27;5;97~");
        Assert.Equal(KeyKind.Char, key.Kind);
        Assert.Equal("a", key.Input);
        Assert.True(key.Ctrl);
        Assert.False(key.Shift);
        Assert.False(key.Meta);
    }

    [Fact]
    public void ModifyOtherKeys_ShiftEnter()
    {
        // 基准 MODIFY_OTHER_KEYS_RE 注释核心例：ESC[27;2;13~ = Shift+Enter
        var key = ParseOne("\u001b[27;2;13~");
        Assert.Equal(KeyKind.Return, key.Kind);
        Assert.True(key.Shift);
        Assert.False(key.Ctrl);
    }

    [Fact]
    public void ModifyOtherKeys_AltBackspace()
    {
        var key = ParseOne("\u001b[27;3;127~");
        Assert.Equal(KeyKind.Backspace, key.Kind);
        Assert.True(key.Meta);
        Assert.False(key.Ctrl);
        Assert.False(key.Shift);
    }

    [Fact]
    public void ModifyOtherKeys_CtrlShiftCombo_PreservesBothModifiers()
    {
        var key = ParseOne("\u001b[27;6;97~");
        Assert.Equal(KeyKind.Char, key.Kind);
        Assert.Equal("a", key.Input);
        Assert.True(key.Ctrl);
        Assert.True(key.Shift);
    }

    [Fact]
    public void ModifyOtherKeys_NoModifier_ParsesBareKeycode()
    {
        // mod 1 = 无修饰（mod-1=0 位掩码）
        var key = ParseOne("\u001b[27;1;97~");
        Assert.Equal(KeyKind.Char, key.Kind);
        Assert.Equal("a", key.Input);
        Assert.False(key.Ctrl);
        Assert.False(key.Meta);
        Assert.False(key.Shift);
    }

    [Theory]
    [InlineData("\u001b[27;5;9~", KeyKind.Tab)]
    [InlineData("\u001b[27;5;13~", KeyKind.Return)]
    public void ModifyOtherKeys_ControlKeycodes_ParseAsNamedKeys(string chunk, KeyKind expected)
    {
        var key = ParseOne(chunk);
        Assert.Equal(expected, key.Kind);
        Assert.True(key.Ctrl);
    }

    [Fact]
    public void ModifyOtherKeys_CtrlEscape_ParsesAsCtrlEscape()
    {
        // 基准 nonAlphanumericKeys 注释场景：ctrl+escape 经 kitty/modifyOtherKeys 通道到达
        var key = ParseOne("\u001b[27;5;27~");
        Assert.Equal(KeyKind.Escape, key.Kind);
        Assert.True(key.Ctrl);
    }

    [Theory]
    [InlineData("\u001b[97")]      // kitty 裸码点无终点
    [InlineData("\u001b[97;5")]     // kitty 修饰段无终点
    [InlineData("\u001b[97;5;")]    // kitty 第三段起点无终点
    [InlineData("\u001b[27;5")]     // modifyOtherKeys 修饰段无终点
    [InlineData("\u001b[27;5;97")]  // modifyOtherKeys 键码段无终点
    public void IncompletePrefix_ReturnsFalseWithoutConsuming(string chunk)
    {
        var span = chunk.AsSpan();
        Assert.False(KeyPressParser.TryParseNext(ref span, out _));
        Assert.Equal(chunk.Length, span.Length);
    }

    [Fact]
    public void KittyEmptyCodepoint_SwallowedAsUnknown()
    {
        // 空码点（ESC[u）非法 — 吞掉防泄漏，后续文本不受污染
        var keys = ParseAll("\u001b[ux");
        Assert.Equal(2, keys.Count);
        Assert.Equal(KeyKind.Unknown, keys[0].Kind);
        Assert.Equal(string.Empty, keys[0].Input);
        Assert.Equal(KeyKind.Char, keys[1].Kind);
        Assert.Equal("x", keys[1].Input);
    }

    [Fact]
    public void KittyZeroModifier_SwallowedAsUnknown()
    {
        // kitty 修饰值域 ≥1（0 非法）— 吞掉
        var key = ParseOne("\u001b[97;0u");
        Assert.Equal(KeyKind.Unknown, key.Kind);
        Assert.Equal(string.Empty, key.Input);
    }

    [Fact]
    public void KittyFlagsQueryResponse_SwallowedAsUnknown()
    {
        // CSI ? flags u 是 kitty 模式查询的终端响应（非按键）— '?' 前缀不进 kitty 按键分支
        var key = ParseOne("\u001b[?1u");
        Assert.Equal(KeyKind.Unknown, key.Kind);
    }

    [Fact]
    public void TwoParamTilde27_NotModifyOtherKeys_StillUnknown()
    {
        // 27;5~ 两段形态不是 modifyOtherKeys（需恰三段）— 维持现有 Unknown 吞掉不泄漏
        var keys = ParseAll("\u001b[27;5~x");
        Assert.Equal(2, keys.Count);
        Assert.Equal(KeyKind.Unknown, keys[0].Kind);
        Assert.Equal(string.Empty, keys[0].Input);
        Assert.Equal(KeyKind.Char, keys[1].Kind);
        Assert.Equal("x", keys[1].Input);
    }

    [Fact]
    public void TraditionalModifiedKeys_UnaffectedByKittyDispatch()
    {
        // kitty 分派不破坏传统两段修饰：1;5D=ctrl+左方向、3;3~=alt+delete
        var left = ParseOne("\u001b[1;5D");
        Assert.Equal(KeyKind.LeftArrow, left.Kind);
        Assert.True(left.Ctrl);

        var del = ParseOne("\u001b[3;3~");
        Assert.Equal(KeyKind.Delete, del.Kind);
        Assert.True(del.Meta);
    }

    [Fact]
    public void KittySequence_CoexistsWithLegacySequences()
    {
        // 同一 chunk 混合 kitty 序列与传统 CSI 序列
        var keys = ParseAll("\u001b[97u\u001b[D");
        Assert.Equal(2, keys.Count);
        Assert.Equal(KeyKind.Char, keys[0].Kind);
        Assert.Equal("a", keys[0].Input);
        Assert.Equal(KeyKind.LeftArrow, keys[1].Kind);
    }

    [Fact]
    public void KittyWithModifiers_CoexistsWithTextAndLegacy()
    {
        var keys = ParseAll("hi\u001b[97;5u\u001b[1;5D");
        Assert.Equal(4, keys.Count);
        Assert.Equal(KeyKind.Char, keys[0].Kind);
        Assert.Equal("h", keys[0].Input);
        Assert.Equal(KeyKind.Char, keys[1].Kind);
        Assert.Equal("i", keys[1].Input);
        Assert.Equal(KeyKind.Char, keys[2].Kind);
        Assert.Equal("a", keys[2].Input);
        Assert.True(keys[2].Ctrl);
        Assert.Equal(KeyKind.LeftArrow, keys[3].Kind);
        Assert.True(keys[3].Ctrl);
    }

    [Fact]
    public void ModifyOtherKeys_CoexistsWithFollowingText()
    {
        var keys = ParseAll("\u001b[27;5;97~x");
        Assert.Equal(2, keys.Count);
        Assert.Equal(KeyKind.Char, keys[0].Kind);
        Assert.Equal("a", keys[0].Input);
        Assert.True(keys[0].Ctrl);
        Assert.Equal(KeyKind.Char, keys[1].Kind);
        Assert.Equal("x", keys[1].Input);
    }
}
