namespace NativeTui.Tests.Repl.Vim;

/// <summary>
/// vim text-object 边界测试 — 基准 src/vim/textObjects.ts 全量：
/// 词对象（iw/aw/iW/aW）、引号对象（i"/a"/i'/a`）、括号对象（i(/a(/ib/ab、[]、iB/aB、&lt;&gt;）。
/// </summary>
public sealed class VimTextObjectTests
{
    [Fact]
    public void InnerWord_OnWordInterior()
    {
        // 词中 → 同类扩展（textObjects.ts:94-96）
        var range = VimTextObject.Find("hello world", 2, 'w', isInner: true);
        Assert.Equal((0, 5), range);
    }

    [Fact]
    public void InnerWord_OnWhitespaceGrapheme_ReturnsWhitespaceRun()
    {
        // 光标落在空白 grapheme 上 → 空白段（基准 offset >= g.index && offset < nextStart 归位）
        var range = VimTextObject.Find("hello world", 5, 'w', isInner: true);
        Assert.Equal((5, 6), range);
    }

    [Fact]
    public void InnerWord_OnPunctuationRun()
    {
        // 标点串成词（textObjects.ts:101-104）
        var range = VimTextObject.Find("a...b", 2, 'w', isInner: true);
        Assert.Equal((1, 4), range);
    }

    [Fact]
    public void WordObject_OnWhitespace_ReturnsWhitespaceRun()
    {
        // 空白对象直接返回空白段（inner = around — textObjects.ts:97-100 提前 return）
        Assert.Equal((5, 6), VimTextObject.Find("hello world", 5, 'w', isInner: true));
        Assert.Equal((5, 6), VimTextObject.Find("hello world", 5, 'w', isInner: false));
        Assert.Equal((1, 3), VimTextObject.Find("a  b", 2, 'w', isInner: true));
    }

    [Fact]
    public void AroundWord_IncludesTrailingWhitespace()
    {
        // aw：优先含尾随空白（textObjects.ts:108-110）
        var range = VimTextObject.Find("hello world", 2, 'w', isInner: false);
        Assert.Equal((0, 6), range);
    }

    [Fact]
    public void AroundWord_LastWordNoTrailing_FallsBackToLeading()
    {
        // 末词无尾随空白 → 含前导空白（textObjects.ts:111-113）
        var range = VimTextObject.Find("hello world", 8, 'w', isInner: false);
        Assert.Equal((5, 11), range);
    }

    [Fact]
    public void BigWord_NonWhitespaceRun()
    {
        // iW/aW：非空白连续段（textObjects.ts:46-47）
        Assert.Equal((0, 7), VimTextObject.Find("foo-bar baz", 2, 'W', isInner: true));
        Assert.Equal((0, 8), VimTextObject.Find("foo-bar baz", 2, 'W', isInner: false));
    }

    [Fact]
    public void WordObject_AtTextEnd_BelongsToLastGrapheme()
    {
        // 光标在文末边界 → 归最后一个 grapheme（基准 graphemeIdx 默认 length-1）
        var range = VimTextObject.Find("hello", 5, 'w', isInner: true);
        Assert.Equal((0, 5), range);
    }

    [Fact]
    public void InnerDoubleQuotes()
    {
        // i"：排除两端引号（textObjects.ts:140-142）
        var text = "say \"hi\" ok";
        Assert.Equal((5, 7), VimTextObject.Find(text, 6, '"', isInner: true));
    }

    [Fact]
    public void AroundDoubleQuotes()
    {
        // a"：含引号（textObjects.ts:142）
        var text = "say \"hi\" ok";
        Assert.Equal((4, 8), VimTextObject.Find(text, 6, '"', isInner: false));
    }

    [Fact]
    public void QuoteObject_OnQuoteCharacter_Hits()
    {
        // 光标在引号上（配对区间含端点 — qs <= pos <= qe）
        var text = "say \"hi\" ok";
        Assert.Equal((4, 8), VimTextObject.Find(text, 4, '"', isInner: false));
        Assert.Equal((4, 8), VimTextObject.Find(text, 7, '"', isInner: false));
    }

    [Fact]
    public void QuoteObject_OutsidePair_ReturnsNull()
    {
        // 引号按顺序配对（0-1、2-3…）— 光标在配对外返回 null（textObjects.ts:135-146）
        var text = "x \"a\" y \"b\" z";
        // 引号位 2/5、8/11；偏移 0 在任何配对外
        Assert.Null(VimTextObject.Find(text, 0, '"', isInner: true));
        // 偏移 13（z 后）也在配对外
        Assert.Null(VimTextObject.Find(text, 13, '"', isInner: true));
    }

    [Fact]
    public void QuoteObject_LimitedToCurrentLine()
    {
        // 引号配对限定行内 — 跨行引号不配对（textObjects.ts:124-127）
        var text = "\"ab\"\n\"cd\"";
        Assert.Equal((6, 8), VimTextObject.Find(text, 6, '"', isInner: true)); // 行 1 的 "cd" 内部
        Assert.Null(VimTextObject.Find(text, 6, '\'', isInner: true)); // 单引号无配对
    }

    [Fact]
    public void QuoteObject_UnevenQuotes_ReturnsNull()
    {
        // 奇数引号无完整配对
        Assert.Null(VimTextObject.Find("a\"bc", 2, '"', isInner: true));
    }

    [Fact]
    public void InnerParentheses()
    {
        // i(：排除括号（textObjects.ts:185）— "(abc)" 光标 'c' 上 → inner [1,4)="abc"
        Assert.Equal((1, 4), VimTextObject.Find("(abc)", 3, '(', isInner: true));
    }

    [Fact]
    public void AroundParentheses()
    {
        // a(：含括号 — around [0,5)="(abc)"
        Assert.Equal((0, 5), VimTextObject.Find("(abc)", 3, '(', isInner: false));
    }

    [Fact]
    public void Parentheses_OnClosingBracket_StillSelectsPair()
    {
        // 光标站在 ')' 上 — 向后扫时 close 不计深度（textObjects.ts:160 `i !== offset`）
        Assert.Equal((0, 5), VimTextObject.Find("(abc)", 4, ')', isInner: false));
        Assert.Equal((0, 5), VimTextObject.Find("(abc)", 4, '(', isInner: false));
        Assert.Equal((1, 4), VimTextObject.Find("(abc)", 4, '(', isInner: true));
    }

    [Fact]
    public void Parentheses_Nested_DepthMatching()
    {
        // 嵌套深度匹配 — 内层括号（textObjects.ts:158-168）
        Assert.Equal((2, 6), VimTextObject.Find("(a(bc)d)", 4, '(', isInner: false));
        // 外层括号（光标在外层内容区）
        Assert.Equal((0, 8), VimTextObject.Find("(a(bc)d)", 1, '(', isInner: false));
    }

    [Fact]
    public void Parentheses_AliasesAndOtherBracketTypes()
    {
        // b=()、B={}（textObjects.ts:19-33 PAIRS）
        Assert.Equal((0, 5), VimTextObject.Find("(abc)", 3, 'b', isInner: false));
        Assert.Equal((0, 5), VimTextObject.Find("{abc}", 3, 'B', isInner: false));
        Assert.Equal((0, 5), VimTextObject.Find("[abc]", 3, ']', isInner: false));
        Assert.Equal((0, 5), VimTextObject.Find("<abc>", 3, '<', isInner: false));
        Assert.Equal((1, 4), VimTextObject.Find("[abc]", 3, '[', isInner: true));
    }

    [Fact]
    public void Parentheses_Unclosed_ReturnsNull()
    {
        // 任一端未闭合 → null（textObjects.ts:169/183）
        Assert.Null(VimTextObject.Find("(abc", 2, '(', isInner: true));
        Assert.Null(VimTextObject.Find("abc)", 2, '(', isInner: true));
    }

    [Fact]
    public void WordObject_EmojiIsPunctuationRun()
    {
        // emoji 多字符 grapheme 属标点串（词分类单字符判定）— 👍 占 [1,3)
        Assert.Equal((1, 3), VimTextObject.Find("a👍b", 2, 'w', isInner: true));
    }

    [Fact]
    public void IsObjectKey_FullSet()
    {
        // 对象类型键全集（types.ts:164-180 TEXT_OBJ_TYPES）
        foreach (var key in "wW\"'`()b[]{}B<>")
        {
            Assert.True(VimTextObject.IsObjectKey(key), $"key {key} 应为对象键");
        }

        Assert.False(VimTextObject.IsObjectKey('a'));
        Assert.False(VimTextObject.IsObjectKey('i'));
        Assert.False(VimTextObject.IsObjectKey('x'));
    }
}
