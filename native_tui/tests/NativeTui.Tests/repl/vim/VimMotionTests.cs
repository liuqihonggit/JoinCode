namespace NativeTui.Tests.Repl.Vim;

/// <summary>
/// vim motion 原语黄金表测试 — 基准 src/vim/motions.ts + src/utils/Cursor.ts 词移动/行定位原语：
/// h/l/j/k、w/b/e、W/B/E、0/^/$、G/gg/goToLine、count 循环、grapheme 边界、f/F/t/T 查找。
/// </summary>
public sealed class VimMotionTests
{
    private const int Width = 80;

    [Fact]
    public void CharLeftRight_StepAndClamp()
    {
        // h/l 逐 grapheme 步进（motions.ts:32-35 — cursor.left()/right()）
        Assert.Equal(2, VimMotions.Resolve("abc", 3, VimMotion.CharLeft, 1, Width));
        Assert.Equal(1, VimMotions.Resolve("abc", 0, VimMotion.CharRight, 1, Width));
        Assert.Equal(0, VimMotions.Resolve("abc", 0, VimMotion.CharLeft, 5, Width));
        Assert.Equal(3, VimMotions.Resolve("abc", 3, VimMotion.CharRight, 5, Width));
    }

    [Fact]
    public void CharLeftRight_CrossNewline()
    {
        // \n 占一个偏移 — 跨行移动（Cursor.left/right 语义）
        Assert.Equal(3, VimMotions.Resolve("ab\ncd", 4, VimMotion.CharLeft, 1, Width));
        Assert.Equal(4, VimMotions.Resolve("ab\ncd", 3, VimMotion.CharRight, 1, Width));
    }

    [Fact]
    public void CharLeftRight_EmojiGraphemeNotSplit()
    {
        // 代理对整块跨（grapheme 语义 — 基准 Cursor.ts:301-319 left/right 即 prevOffset/nextOffset）：
        // "a👍b" 边界 0,1,3,4 — h(3)→1 跳过整个 👍 不落中间 2；h(1)→0；l(1)→3
        var text = "a👍b";
        Assert.Equal(1, VimMotions.Resolve(text, 3, VimMotion.CharLeft, 1, Width));
        Assert.Equal(0, VimMotions.Resolve(text, 1, VimMotion.CharLeft, 1, Width));
        Assert.Equal(3, VimMotions.Resolve(text, 1, VimMotion.CharRight, 1, Width));
    }

    [Fact]
    public void LineUpDown_KeepColumnAndClamp()
    {
        // j/k 逻辑行同列 + 短行钳制行尾（Cursor.ts:511-553）
        var text = "aaa\nb\nccccc";
        Assert.Equal(5, VimMotions.Resolve(text, 2, VimMotion.LineDown, 1, Width)); // 列 2 → 中间行 "b" 钳列 1 → 偏移 5
        Assert.Equal(1, VimMotions.Resolve(text, 5, VimMotion.LineUp, 1, Width)); // 列 1 → 上一行列 1 → 偏移 1
    }

    [Fact]
    public void LineDown_ShortLineClampsToLineEnd()
    {
        // "aaa\nb\nccccc" 光标 2（列 2）j → 中间行 "b"（列钳 1）→ 偏移 4+1=5
        var text = "aaa\nb\nccccc";
        Assert.Equal(5, VimMotions.Resolve(text, 2, VimMotion.LineDown, 1, Width));
        // 再 j → 末行列 1 → 偏移 6+1=7
        Assert.Equal(7, VimMotions.Resolve(text, 5, VimMotion.LineDown, 1, Width));
    }

    [Fact]
    public void LineUp_FirstLineStaysAtZero_AndLastLineStaysAtEnd()
    {
        var text = "aaa\nb\nccccc";
        Assert.Equal(0, VimMotions.Resolve(text, 1, VimMotion.LineUp, 3, Width)); // 首行回 0（Cursor.ts:514-517）
        // 末行 j 回文末 len=11（Cursor.ts:537-538 `currentEnd >= text.length → text.length`）— 与 $ 落 \n 位同类的基准怪行为，不回退到最后字符
        Assert.Equal(11, VimMotions.Resolve(text, 7, VimMotion.LineDown, 3, Width));
    }

    [Fact]
    public void LineUpDown_EmojiColumnSnapsToGraphemeBoundary()
    {
        // 列钳制后 grapheme 边界对齐（createCursorWithColumn — Cursor.ts:483-493）
        var text = "👍👍\nab"; // 行 0 宽 4 char / 行 1 长 2
        // k 起点须在行 1（≥5）：行 1 文末偏移 7（列 2）→ 钳列 2 → 偏移 2（第二 emoji 起点，恰为 grapheme 边界）
        // （偏移 4 是 \n 位属行 0 首行 — k 回 0，Cursor.ts:514-517）
        Assert.Equal(2, VimMotions.Resolve(text, 7, VimMotion.LineUp, 1, Width));
    }

    [Fact]
    public void WordForward_WalksWordPunctuationAndWhitespace()
    {
        // w：词=字母数字下划线串或标点串，跨空白（Cursor.ts:658-689 nextVimWord）
        var text = "foo.bar baz";
        Assert.Equal(3, VimMotions.Resolve(text, 0, VimMotion.WordForward, 1, Width)); // foo → '.'（标点串成词）
        Assert.Equal(4, VimMotions.Resolve(text, 3, VimMotion.WordForward, 1, Width)); // '.' → bar
        Assert.Equal(8, VimMotions.Resolve(text, 4, VimMotion.WordForward, 1, Width)); // bar → baz
        Assert.Equal(11, VimMotions.Resolve(text, 8, VimMotion.WordForward, 1, Width)); // baz → 文末
    }

    [Fact]
    public void WordForward_CountAccumulates()
    {
        // count 前缀循环（motions.ts:13-25 resolveMotion）：2w=第二词首 8；3w=第三词首 11；越界钳文末
        Assert.Equal(8, VimMotions.Resolve("foo bar baz", 0, VimMotion.WordForward, 2, Width));
        Assert.Equal(11, VimMotions.Resolve("foo bar baz", 0, VimMotion.WordForward, 3, Width));
        Assert.Equal(11, VimMotions.Resolve("foo bar baz", 0, VimMotion.WordForward, 10, Width)); // 越界钳文末
    }

    [Fact]
    public void WordForward_CrossesNewline()
    {
        // 换行属空白 — 词移动跨行（基准 \s 语义，Cursor.ts:681-686）
        // 2w：0→3（cd 词首，跨 \n）→5（文末 — 第二次 w 跳过 cd 到 len）
        Assert.Equal(5, VimMotions.Resolve("ab\ncd", 0, VimMotion.WordForward, 2, Width)); // ab → 越行到 cd → 文末
    }

    [Fact]
    public void WordBack_WalksBackward()
    {
        // b：反向词首（Cursor.ts:737-772 prevVimWord）
        var text = "foo.bar baz";
        Assert.Equal(8, VimMotions.Resolve(text, 11, VimMotion.WordBack, 1, Width)); // 尾 → baz 首
        Assert.Equal(4, VimMotions.Resolve(text, 8, VimMotion.WordBack, 1, Width)); // baz → bar 首
        Assert.Equal(0, VimMotions.Resolve(text, 3, VimMotion.WordBack, 2, Width)); // 标点 → foo 首
    }

    [Fact]
    public void WordEnd_LandsOnLastGraphemeStart()
    {
        // e：光标落词尾字符上（Cursor.ts:691-735 endOfVimWord）
        var text = "foo.bar baz";
        Assert.Equal(2, VimMotions.Resolve(text, 0, VimMotion.WordEnd, 1, Width)); // foo → 'o'（偏移 2）
        Assert.Equal(3, VimMotions.Resolve(text, 2, VimMotion.WordEnd, 1, Width)); // → '.'（偏移 3）
        Assert.Equal(6, VimMotions.Resolve(text, 3, VimMotion.WordEnd, 1, Width)); // → bar 尾 'r'（偏移 6）
    }

    [Fact]
    public void WordEnd_SkipsWhitespaceToNextWord()
    {
        // e 落词尾字符上（Cursor.ts:691-735 endOfVimWord）：跳过当前 grapheme + 空白段后推进到目标词尾 grapheme 起点
        var text = "foo  bar";
        Assert.Equal(7, VimMotions.Resolve(text, 2, VimMotion.WordEnd, 1, Width)); // foo 尾 → bar 尾 'r'（偏移 7）
    }

    [Fact]
    public void BigWordMotions_NonWhitespaceRuns()
    {
        // W/B/E：大词=非空白连续段（Cursor.ts:774-843）
        var text = "foo-bar baz";
        Assert.Equal(8, VimMotions.Resolve(text, 0, VimMotion.BigWordForward, 1, Width)); // foo-bar 整段 → baz
        Assert.Equal(0, VimMotions.Resolve(text, 8, VimMotion.BigWordBack, 1, Width));
        Assert.Equal(6, VimMotions.Resolve(text, 0, VimMotion.BigWordEnd, 1, Width)); // 大词尾 'r'（偏移 6）
    }

    [Fact]
    public void BigWordEnd_AtWordEndMovesToNextWordEnd()
    {
        // 已在大词尾 → 跳下一大词尾（Cursor.ts:798-806 递归语义）
        // 基准 endOfWORD 推进循环（Cursor.ts:814 `!cursor.right().isOverWhitespace()`）：目标词为末词时
        // right() 越界 isOverWhitespace=false（Cursor.ts:986-989 越界 ?? '' 不匹配）→ 冲过词尾落 len=5 — 既有怪行为
        Assert.Equal(5, VimMotions.Resolve("ab cd", 1, VimMotion.BigWordEnd, 1, Width)); // ab 尾 → 递归下一词 → 冲到文末 5
        Assert.Equal(1, VimMotions.Resolve("ab cd", 0, VimMotion.BigWordEnd, 1, Width)); // ab 内 → ab 尾 'b'（偏移 1）
    }

    [Fact]
    public void LinePositions_StartFirstNonBlankEnd()
    {
        // 0/^/$（motions.ts:56-61 — startOfLogicalLine/firstNonBlankInLogicalLine/endOfLogicalLine）
        var text = "  ab cd";
        Assert.Equal(0, VimMotions.Resolve(text, 5, VimMotion.LineStart, 1, Width));
        Assert.Equal(2, VimMotions.Resolve(text, 5, VimMotion.FirstNonBlank, 1, Width));
        Assert.Equal(7, VimMotions.Resolve(text, 2, VimMotion.LineEnd, 1, Width)); // 文末（无 \n）
    }

    [Fact]
    public void LineEnd_MultilineLandsOnNewlineOrTextEnd()
    {
        // $ 落 \n 位（基准 endOfLogicalLine = findLogicalLineEnd 语义）
        var text = "ab\ncd";
        Assert.Equal(2, VimMotions.Resolve(text, 0, VimMotion.LineEnd, 1, Width)); // 行 0 → \n 位 2
        Assert.Equal(5, VimMotions.Resolve(text, 4, VimMotion.LineEnd, 1, Width)); // 末行 → 文末 5
    }

    [Fact]
    public void G_LastLineAndGoToLine()
    {
        // G：末行首 / 第 N 行首（Cursor.ts:1009-1032 — 1-indexed 钳制）
        var text = "aa\nbb\ncc\ndd";
        Assert.Equal(9, VimMotions.LastLineStart(text));
        Assert.Equal(0, VimMotions.GoToLine(text, 1));
        Assert.Equal(6, VimMotions.GoToLine(text, 3));
        Assert.Equal(9, VimMotions.GoToLine(text, 10)); // 越界钳末行
    }

    [Fact]
    public void VisualLineUpDown_WrappedRows()
    {
        // gj/gk：折行视觉行同显示列（Cursor.ts:353-408 up/down）
        var text = "abcdefgh"; // wrapWidth=5 → 视觉行 [0..5) [5..8)
        Assert.Equal(7, VimMotions.Resolve(text, 2, VimMotion.VisualLineDown, 1, 5));
        Assert.Equal(2, VimMotions.Resolve(text, 7, VimMotion.VisualLineUp, 1, 5));
        Assert.Equal(2, VimMotions.Resolve(text, 2, VimMotion.VisualLineUp, 1, 5)); // 首视觉行不动
        Assert.Equal(7, VimMotions.Resolve(text, 7, VimMotion.VisualLineDown, 1, 5)); // 末视觉行不动
    }

    [Fact]
    public void VisualLineDown_ShortRowClamps()
    {
        // 目标视觉行较短 → 钳行尾（基准 down(): column > width → width）
        var text = "abcdefgh"; // wrapWidth=5 → [0..5) 长 5 / [5..8) 长 3
        Assert.Equal(8, VimMotions.Resolve(text, 4, VimMotion.VisualLineDown, 1, 5)); // 显示列 4 超短行宽 3 → 钳行尾 8
    }

    [Fact]
    public void FindCharacter_ForwardBackwardTill()
    {
        // f/F/t/T + count（Cursor.ts:1062-1105）
        var text = "aXbXc";
        Assert.Equal(1, VimMotions.FindCharacter(text, 0, "X", VimFindKind.ToForward, 1));
        Assert.Equal(3, VimMotions.FindCharacter(text, 0, "X", VimFindKind.ToForward, 2));
        Assert.Null(VimMotions.FindCharacter(text, 0, "X", VimFindKind.ToForward, 3));
        Assert.Equal(3, VimMotions.FindCharacter(text, 4, "X", VimFindKind.ToBackward, 1));
        // 光标自身不算且其前无目标（prevOffset(1)=0 是 'a'）→ null（Cursor.ts:1086-1101 反向循环尽 → return null）
        Assert.Null(VimMotions.FindCharacter(text, 1, "X", VimFindKind.ToBackward, 1));
        // t：贴目标前一 grapheme（Cursor.ts:1079-1081 `max(offset, prevOffset(pos))`）— 'b' 前 1 位 = 1
        Assert.Equal(1, VimMotions.FindCharacter(text, 0, "b", VimFindKind.TillForward, 1));
        // T：贴目标后一 grapheme（Cursor.ts:1094-1096 `min(offset, nextOffset(pos))`）— 'b'(2) 后 1 位 = 3
        Assert.Equal(3, VimMotions.FindCharacter(text, 4, "b", VimFindKind.TillBackward, 1));
    }

    [Fact]
    public void FindCharacter_EmptyTargetNeverMatches()
    {
        // 空目标字符与任何 grapheme 不等（基准 grapheme === '' 恒 false）
        Assert.Null(VimMotions.FindCharacter("abc", 0, string.Empty, VimFindKind.ToForward, 1));
    }

    [Fact]
    public void InclusiveLinewiseClassification()
    {
        // motions.ts:72-82：e/E/$ inclusive；j/k/G/gg linewise（gj/gk 不算）
        Assert.True(VimMotions.IsInclusive(VimMotion.WordEnd));
        Assert.True(VimMotions.IsInclusive(VimMotion.BigWordEnd));
        Assert.True(VimMotions.IsInclusive(VimMotion.LineEnd));
        Assert.False(VimMotions.IsInclusive(VimMotion.WordForward));
        Assert.False(VimMotions.IsInclusive(VimMotion.LineStart));
        Assert.True(VimMotions.IsLinewise(VimMotion.LineDown));
        Assert.True(VimMotions.IsLinewise(VimMotion.LineUp));
        Assert.True(VimMotions.IsLinewise(VimMotion.BufferEnd));
        Assert.True(VimMotions.IsLinewise(VimMotion.FirstLine));
        Assert.False(VimMotions.IsLinewise(VimMotion.VisualLineDown));
        Assert.False(VimMotions.IsLinewise(VimMotion.VisualLineUp));
    }

    [Fact]
    public void WordCharClassification()
    {
        // 词字符分类（Cursor.ts:134-143）：字母/数字/下划线/记号单字符；emoji 多字符 grapheme 属标点
        Assert.True(VimMotions.IsVimWordChar("a"));
        Assert.True(VimMotions.IsVimWordChar("Z"));
        Assert.True(VimMotions.IsVimWordChar("9"));
        Assert.True(VimMotions.IsVimWordChar("_"));
        Assert.False(VimMotions.IsVimWordChar("."));
        Assert.False(VimMotions.IsVimWordChar(" "));
        Assert.False(VimMotions.IsVimWordChar("👍")); // 多字符 grapheme（基准正则锚定单字符）
        Assert.True(VimMotions.IsVimWhitespace(" "));
        Assert.True(VimMotions.IsVimWhitespace("\n"));
        Assert.False(VimMotions.IsVimWhitespace("a"));
        Assert.True(VimMotions.IsVimPunctuation("."));
        Assert.True(VimMotions.IsVimPunctuation("👍"));
        Assert.True(VimMotions.IsVimPunctuation("ab")); // 多字符串整体判定：非词非空白（基准正则锚定单字符 — 同样 true）
    }

    [Fact]
    public void GraphemeHelpers_LastGraphemeLength()
    {
        // 末 grapheme 长度（operators maxOff 钳制依据 — 基准 lastGrapheme().length || 1）
        Assert.Equal(1, VimMotions.LastGraphemeLength("abc"));
        Assert.Equal(2, VimMotions.LastGraphemeLength("a👍"));
        Assert.Equal(1, VimMotions.LastGraphemeLength(string.Empty)); // 空文本回 1（基准 || 1）
    }
}
