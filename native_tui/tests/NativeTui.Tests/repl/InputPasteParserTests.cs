namespace NativeTui.Tests.Repl;

/// <summary>
/// bracketed paste 解析测试 — 对齐基准 DEC 2004 模式（\x1b[200~ 内容 \x1b[201~）：
/// 整块产出 Paste 事件（Input=粘贴原文），终止标记未到不消耗（跨 chunk 攒齐），
/// 块后按键继续解析；另覆盖 Ctrl+_（chat:undo 键位）归一与 ReadKey 路径 Paste 工厂。
/// </summary>
public sealed class InputPasteParserTests
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

    [Fact]
    public void BracketedPasteBlock_ProducesPasteEventWithContent()
    {
        var keys = ParseAll("\u001b[200~hello world\u001b[201~");
        var key = Assert.Single(keys);
        Assert.Equal(KeyKind.Paste, key.Kind);
        Assert.Equal("hello world", key.Input);
        Assert.False(key.Ctrl);
        Assert.False(key.Meta);
        Assert.False(key.Shift);
    }

    [Fact]
    public void EmptyPasteBlock_ProducesPasteEventWithEmptyContent()
    {
        var keys = ParseAll("\u001b[200~\u001b[201~");
        var key = Assert.Single(keys);
        Assert.Equal(KeyKind.Paste, key.Kind);
        Assert.Equal(string.Empty, key.Input);
    }

    [Fact]
    public void PasteBlockWithNewlines_KeepsMultilineContentIntact()
    {
        var keys = ParseAll("\u001b[200~line1\nline2\nline3\u001b[201~");
        var key = Assert.Single(keys);
        Assert.Equal("line1\nline2\nline3", key.Input);
    }

    [Fact]
    public void IncompletePasteBlock_ReturnsFalseWithoutConsuming()
    {
        var span = "\u001b[200~partial content".AsSpan();
        Assert.False(KeyPressParser.TryParseNext(ref span, out _));
        Assert.Equal("\u001b[200~partial content".Length, span.Length);
    }

    [Fact]
    public void PasteBlockWithoutEndMarker_PrefixOnly_ReturnsFalse()
    {
        var span = "\u001b[200~".AsSpan();
        Assert.False(KeyPressParser.TryParseNext(ref span, out _));
        Assert.Equal("\u001b[200~".Length, span.Length);
    }

    [Fact]
    public void KeysAfterPasteBlock_ContinueParsing()
    {
        var keys = ParseAll("a\u001b[200~pasted\u001b[201~\u001b[D");
        Assert.Equal(3, keys.Count);
        Assert.Equal(KeyKind.Char, keys[0].Kind);
        Assert.Equal("a", keys[0].Input);
        Assert.Equal(KeyKind.Paste, keys[1].Kind);
        Assert.Equal("pasted", keys[1].Input);
        Assert.Equal(KeyKind.LeftArrow, keys[2].Kind);
    }

    [Fact]
    public void PasteContentWithInnerEscapeSequences_KeptVerbatimUntilEndMarker()
    {
        var keys = ParseAll("\u001b[200~\u001b[1;3D inside\u001b[201~");
        var key = Assert.Single(keys);
        Assert.Equal("\u001b[1;3D inside", key.Input);
    }

    [Fact]
    public void NestedStartMarker_KeptVerbatimUntilFirstEndMarker()
    {
        var keys = ParseAll("\u001b[200~outer \u001b[200~inner\u001b[201~");
        var key = Assert.Single(keys);
        Assert.Equal("outer \u001b[200~inner", key.Input);
    }

    [Fact]
    public void LoneEndMarker_SwallowedAsUnknown()
    {
        var keys = ParseAll("\u001b[201~x");
        Assert.Equal(2, keys.Count);
        Assert.Equal(KeyKind.Unknown, keys[0].Kind);
        Assert.Equal(KeyKind.Char, keys[1].Kind);
    }

    [Fact]
    public void CtrlUnderscore_ParsesAsCtrlName()
    {
        var key = ParseAll("\u001f")[0];
        Assert.Equal(KeyKind.Char, key.Kind);
        Assert.Equal("_", key.Input);
        Assert.True(key.Ctrl);
    }

    [Fact]
    public void ConsoleKeyAdapter_PasteFactory_ProducesPasteEvent()
    {
        var key = ConsoleKeyAdapter.Paste("factory content");
        Assert.Equal(KeyKind.Paste, key.Kind);
        Assert.Equal("factory content", key.Input);
    }
}
