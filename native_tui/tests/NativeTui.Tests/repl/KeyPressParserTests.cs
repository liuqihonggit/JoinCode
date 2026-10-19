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
}
