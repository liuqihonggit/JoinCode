namespace NativeTui.Tests.Repl;

/// <summary>
/// 按键解码器测试 — 对齐基准 ink/parse-keypress + useTextInput 的序列语义：
/// 转义序列（方向键/Home/End/Delete/Page）、Ctrl 字母、Meta 前缀、chunk 边界。
/// 契约：TryParseNext 把 buffer 视为当前全部可用输入 — chunk 末尾的孤立 ESC 解析为 Escape，
/// 不完整前缀返回 false（等待更多输入）。
/// </summary>
public sealed class KeyPressParserTests
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
    public void PrintableChar_ParsesAsChar()
    {
        var key = ParseOne("a");
        Assert.Equal(KeyKind.Char, key.Kind);
        Assert.Equal("a", key.Input);
        Assert.False(key.Ctrl);
        Assert.False(key.Meta);
        Assert.False(key.Shift);
    }

    [Fact]
    public void UppercaseLetter_SetsShift()
    {
        var key = ParseOne("A");
        Assert.Equal(KeyKind.Char, key.Kind);
        Assert.Equal("A", key.Input);
        Assert.True(key.Shift);
    }

    [Theory]
    [InlineData("\r")]
    [InlineData("\n")]
    public void Newline_ParsesAsReturn(string chunk)
    {
        var key = ParseOne(chunk);
        Assert.Equal(KeyKind.Return, key.Kind);
    }

    [Fact]
    public void Tab_ParsesAsTab()
    {
        var key = ParseOne("\t");
        Assert.Equal(KeyKind.Tab, key.Kind);
    }

    [Fact]
    public void EscZ_ParsesAsBackTab()
    {
        var key = ParseOne("\x1b[Z");
        Assert.Equal(KeyKind.BackTab, key.Kind);
        Assert.True(key.Shift);
    }

    [Fact]
    public void Del_ParsesAsBackspace()
    {
        var key = ParseOne("\u007f");
        Assert.Equal(KeyKind.Backspace, key.Kind);
    }

    [Fact]
    public void CtrlH_ParsesAsBackspace()
    {
        var key = ParseOne("\u0008");
        Assert.Equal(KeyKind.Backspace, key.Kind);
    }

    [Theory]
    [InlineData("\u0001", 'a')]
    [InlineData("\u0002", 'b')]
    [InlineData("\u0003", 'c')]
    [InlineData("\u0005", 'e')]
    [InlineData("\u0006", 'f')]
    [InlineData("\u000b", 'k')]
    [InlineData("\u0010", 'p')]
    [InlineData("\u0015", 'u')]
    [InlineData("\u0017", 'w')]
    [InlineData("\u0019", 'y')]
    public void CtrlLetter_ParsesAsCtrlName(string chunk, char expected)
    {
        var key = ParseOne(chunk);
        Assert.Equal(KeyKind.Char, key.Kind);
        Assert.True(key.Ctrl);
        Assert.Equal(expected.ToString(), key.Input);
    }

    [Theory]
    [InlineData("\u001b[A", KeyKind.UpArrow)]
    [InlineData("\u001b[B", KeyKind.DownArrow)]
    [InlineData("\u001b[C", KeyKind.RightArrow)]
    [InlineData("\u001b[D", KeyKind.LeftArrow)]
    [InlineData("\u001bOA", KeyKind.UpArrow)]
    [InlineData("\u001bOB", KeyKind.DownArrow)]
    [InlineData("\u001bOC", KeyKind.RightArrow)]
    [InlineData("\u001bOD", KeyKind.LeftArrow)]
    public void ArrowSequences_ParseWithEmptyInput(string chunk, KeyKind expected)
    {
        var key = ParseOne(chunk);
        Assert.Equal(expected, key.Kind);
        Assert.Equal(string.Empty, key.Input);
    }

    [Theory]
    [InlineData("\u001b[H")]
    [InlineData("\u001b[1~")]
    [InlineData("\u001bOH")]
    public void HomeSequences_ParseAsHome(string chunk)
    {
        Assert.Equal(KeyKind.Home, ParseOne(chunk).Kind);
    }

    [Theory]
    [InlineData("\u001b[F")]
    [InlineData("\u001b[4~")]
    [InlineData("\u001bOF")]
    public void EndSequences_ParseAsEnd(string chunk)
    {
        Assert.Equal(KeyKind.End, ParseOne(chunk).Kind);
    }

    [Theory]
    [InlineData("\u001b[3~", KeyKind.Delete)]
    [InlineData("\u001b[5~", KeyKind.PageUp)]
    [InlineData("\u001b[6~", KeyKind.PageDown)]
    public void TildeSequences_ParseAsNamedKeys(string chunk, KeyKind expected)
    {
        Assert.Equal(expected, ParseOne(chunk).Kind);
    }

    [Fact]
    public void CtrlModifiedArrow_SetsCtrl()
    {
        var key = ParseOne("\u001b[1;5D");
        Assert.Equal(KeyKind.LeftArrow, key.Kind);
        Assert.True(key.Ctrl);
        Assert.False(key.Meta);
    }

    [Fact]
    public void AltModifiedDelete_SetsMeta()
    {
        var key = ParseOne("\u001b[3;3~");
        Assert.Equal(KeyKind.Delete, key.Kind);
        Assert.True(key.Meta);
        Assert.False(key.Ctrl);
    }

    [Fact]
    public void ShiftModifiedArrow_SetsShift()
    {
        var key = ParseOne("\u001b[1;2C");
        Assert.Equal(KeyKind.RightArrow, key.Kind);
        Assert.True(key.Shift);
    }

    [Theory]
    [InlineData("\u001bb", 'b')]
    [InlineData("\u001bf", 'f')]
    [InlineData("\u001bd", 'd')]
    [InlineData("\u001by", 'y')]
    public void MetaLetter_ParsesAsMetaWithChar(string chunk, char expected)
    {
        var key = ParseOne(chunk);
        Assert.Equal(KeyKind.Char, key.Kind);
        Assert.True(key.Meta);
        Assert.False(key.Ctrl);
        Assert.Equal(expected.ToString(), key.Input);
    }

    [Fact]
    public void MetaBackspace_Parses()
    {
        var key = ParseOne("\u001b\u007f");
        Assert.Equal(KeyKind.Backspace, key.Kind);
        Assert.True(key.Meta);
    }

    [Fact]
    public void MetaReturn_Parses()
    {
        var key = ParseOne("\u001b\r");
        Assert.Equal(KeyKind.Return, key.Kind);
        Assert.True(key.Meta);
    }

    [Fact]
    public void LoneEscape_AtChunkEnd_ParsesAsEscape()
    {
        var key = ParseOne("\u001b");
        Assert.Equal(KeyKind.Escape, key.Kind);
    }

    [Fact]
    public void FocusInSequence_ParsesAsFocusIn()
    {
        // 基准 DECSET 1004 焦点上报：CSI I = 获得焦点（terminal-focus-state.ts setTerminalFocused(true)）
        var key = ParseOne("\u001b[I");
        Assert.Equal(KeyKind.FocusIn, key.Kind);
    }

    [Fact]
    public void FocusOutSequence_ParsesAsFocusOut()
    {
        // CSI O = 失去焦点 — 注意与 SS3 应用模式（ESC O final，如 ESC OA=上方向键）前缀区分：
        // 焦点序列是 ESC [ O（CSI 带方括号），方向键是 ESC O A（SS3 无方括号）
        var key = ParseOne("\u001b[O");
        Assert.Equal(KeyKind.FocusOut, key.Kind);
    }

    [Fact]
    public void FocusSequence_DoesNotConflateWithSs3()
    {
        // SS3 上方向键（ESC O A）不受焦点序列影响
        var key = ParseOne("\u001bOA");
        Assert.Equal(KeyKind.UpArrow, key.Kind);
    }

    [Fact]
    public void IncompletePrefix_ReturnsFalseWithoutConsuming()
    {
        var span = "\u001b[".AsSpan();
        Assert.False(KeyPressParser.TryParseNext(ref span, out _));
        Assert.Equal(2, span.Length);
    }

    [Fact]
    public void IncompleteModifierPrefix_ReturnsFalse()
    {
        var span = "\u001b[1;".AsSpan();
        Assert.False(KeyPressParser.TryParseNext(ref span, out _));
    }

    [Fact]
    public void CoalescedChunk_ParsesEachKey()
    {
        var keys = ParseAll("hi\u001b[D");
        Assert.Equal(3, keys.Count);
        Assert.Equal(KeyKind.Char, keys[0].Kind);
        Assert.Equal("h", keys[0].Input);
        Assert.Equal(KeyKind.Char, keys[1].Kind);
        Assert.Equal("i", keys[1].Input);
        Assert.Equal(KeyKind.LeftArrow, keys[2].Kind);
    }

    [Fact]
    public void UnknownTildeSequence_ConsumedAsUnknown()
    {
        var keys = ParseAll("\u001b[25~x");
        Assert.Equal(2, keys.Count);
        Assert.Equal(KeyKind.Unknown, keys[0].Kind);
        Assert.Equal(string.Empty, keys[0].Input);
        Assert.Equal(KeyKind.Char, keys[1].Kind);
        Assert.Equal("x", keys[1].Input);
    }

    [Fact]
    public void UnknownEscapeSequence_SwallowsWithoutLeaking()
    {
        var keys = ParseAll("\u001b[<64;74;16M");
        var key = Assert.Single(keys);
        Assert.Equal(KeyKind.Unknown, key.Kind);
        Assert.Equal(string.Empty, key.Input);
    }

    [Fact]
    public void OtherControlChars_ParseAsUnknown()
    {
        var key = ParseOne("\u0000");
        Assert.Equal(KeyKind.Unknown, key.Kind);
    }

    [Fact]
    public void CjkChar_ParsesAsChar()
    {
        var key = ParseOne("你");
        Assert.Equal(KeyKind.Char, key.Kind);
        Assert.Equal("你", key.Input);
    }

    [Fact]
    public void EmptyNumberParam_DoesNotThrow()
    {
        // ESC[;5A — xterm 空参数（默认值 1）+ 修饰 5 = Ctrl+Up；基准 parseInt("")→NaN 永不抛
        var key = ParseOne("\u001b[;5A");
        Assert.Equal(KeyKind.UpArrow, key.Kind);
        Assert.True(key.Ctrl);
    }

    [Fact]
    public void OverflowingNumberParam_DoesNotThrowOrLeak()
    {
        // 超长数字参数 — 基准 parseInt 返回数值不抛；C# int.Parse 曾抛 OverflowException 击穿读线程
        var keys = ParseAll("\u001b[9999999999999999~x");
        Assert.Equal(2, keys.Count);
        Assert.Equal(KeyKind.Unknown, keys[0].Kind);
        Assert.Equal(string.Empty, keys[0].Input);
        Assert.Equal(KeyKind.Char, keys[1].Kind);
        Assert.Equal("x", keys[1].Input);
    }

    [Fact]
    public void ModifyOtherKeysThreePartSequence_SwallowsWithoutLeaking()
    {
        // ESC[27;2;13~ = modifyOtherKeys Shift+Enter（SSH/终端预配置场景真实到达）
        // 契约：无法识别的序列整体吞掉，禁止碎片泄漏为文本
        var keys = ParseAll("\u001b[27;2;13~x");
        Assert.Equal(2, keys.Count);
        Assert.Equal(KeyKind.Unknown, keys[0].Kind);
        Assert.Equal(string.Empty, keys[0].Input);
        Assert.Equal(KeyKind.Char, keys[1].Kind);
        Assert.Equal("x", keys[1].Input);
    }

    [Fact]
    public void PrivateParamSequence_SwallowsWithoutLeaking()
    {
        // ESC[?1$y = DECRPM 终端响应（私有参数 + intermediate 字节）— 整体吞噬禁止 1$y 泄漏
        var keys = ParseAll("\u001b[?1$y" + "a");
        Assert.Equal(2, keys.Count);
        Assert.Equal(KeyKind.Unknown, keys[0].Kind);
        Assert.Equal(string.Empty, keys[0].Input);
        Assert.Equal(KeyKind.Char, keys[1].Kind);
        Assert.Equal("a", keys[1].Input);
    }

    [Fact]
    public void OverflowingModifierParam_DoesNotThrow()
    {
        var keys = ParseAll("\u001b[1;9999999999999999D" + "b");
        Assert.Equal(2, keys.Count);
        Assert.Equal(KeyKind.Unknown, keys[0].Kind);
        Assert.Equal(string.Empty, keys[0].Input);
        Assert.Equal(KeyKind.Char, keys[1].Kind);
        Assert.Equal("b", keys[1].Input);
    }

    /// <summary>功能键域覆盖（基准 parse-keypress.ts:304-406 keyName 全表）：
    /// F1-F12 三种变体（SS3 OP-OS / ~数字表 / linux console [[A-E）+ Insert/Clear + rxvt Home/End。</summary>
    [Theory]
    [InlineData("\u001bOP", KeyKind.F1)]
    [InlineData("\u001bOQ", KeyKind.F2)]
    [InlineData("\u001bOR", KeyKind.F3)]
    [InlineData("\u001bOS", KeyKind.F4)]
    [InlineData("\u001b[11~", KeyKind.F1)]
    [InlineData("\u001b[12~", KeyKind.F2)]
    [InlineData("\u001b[13~", KeyKind.F3)]
    [InlineData("\u001b[14~", KeyKind.F4)]
    [InlineData("\u001b[15~", KeyKind.F5)]
    [InlineData("\u001b[17~", KeyKind.F6)]
    [InlineData("\u001b[18~", KeyKind.F7)]
    [InlineData("\u001b[19~", KeyKind.F8)]
    [InlineData("\u001b[20~", KeyKind.F9)]
    [InlineData("\u001b[21~", KeyKind.F10)]
    [InlineData("\u001b[23~", KeyKind.F11)]
    [InlineData("\u001b[24~", KeyKind.F12)]
    [InlineData("\u001b[[A", KeyKind.F1)]
    [InlineData("\u001b[[B", KeyKind.F2)]
    [InlineData("\u001b[[C", KeyKind.F3)]
    [InlineData("\u001b[[D", KeyKind.F4)]
    [InlineData("\u001b[[E", KeyKind.F5)]
    [InlineData("\u001b[2~", KeyKind.Insert)]
    [InlineData("\u001b[7~", KeyKind.Home)]
    [InlineData("\u001b[8~", KeyKind.End)]
    [InlineData("\u001b[E", KeyKind.Clear)]
    [InlineData("\u001bOE", KeyKind.Clear)]
    public void FunctionKeySequences_ParseAsNamedKeys(string chunk, KeyKind expected)
    {
        Assert.Equal(expected, ParseOne(chunk).Kind);
    }

    /// <summary>超时 flush（基准 App.tsx:309-333 flushIncomplete — 50ms NORMAL_TIMEOUT）：
    /// 序列后半段永不到达（慢链路丢失）时，孤立 ESC 前缀强制产出 Escape、剩余字节下轮按文本解析
    ///（非 force 路径永久滞留 pending = 输入静默失联）。</summary>
    [Theory]
    [InlineData("\u001b[")]
    [InlineData("\u001b[1;")]
    [InlineData("\u001bO")]
    public void ForcedFlush_IncompleteEscapePrefix_ProducesEscape(string chunk)
    {
        var span = chunk.AsSpan();
        Assert.True(KeyPressParser.TryParseNext(ref span, out var key, force: true));
        Assert.Equal(KeyKind.Escape, key.Kind);
        // 消耗 1 字节（仅 ESC）— 剩余前缀下轮按文本逐字符解析
        Assert.Equal(chunk[1..], span.ToString());
    }

    /// <summary>超时 flush（基准 App.tsx:121,356 — 500ms PASTE_TIMEOUT）：
    /// 粘贴终止标记永不到达时，已有部分内容强制产出 Paste 事件（基准 emit pasteBuffer 释放通道
    ///——非 force 路径整块永久滞留 = 输入静默失联）。</summary>
    [Fact]
    public void ForcedFlush_IncompletePaste_ProducesPartialContent()
    {
        var span = "\u001b[200~partial content".AsSpan();
        Assert.True(KeyPressParser.TryParseNext(ref span, out var key, force: true));
        Assert.Equal(KeyKind.Paste, key.Kind);
        Assert.Equal("partial content", key.Input);
        Assert.True(span.IsEmpty);
    }
}
