namespace NativeTui.Tests.Repl.Vim;

/// <summary>
/// vim 算子执行测试 — 基准 src/vim/operators.ts 全量（经 VimEngine NORMAL 按键驱动）：
/// d/c/y × motion/find/text-object 组合、dd/cc/yy 行级、x/r/~/J/p/P/o/O/缩进独立命令、
/// inclusive/linewise 范围语义、删除后光标 maxOff 钳制、寄存器写入。
/// </summary>
public sealed class VimOperatorTests
{
    /// <summary>NORMAL 模式引擎工厂。</summary>
    private static VimEngine Normal(string text, int offset) =>
        VimEngine.Of(text, offset, VimMode.Normal);

    /// <summary>单字符按键快捷。</summary>
    private static KeyPress Key(string input) => KeyPress.Char(input);

    /// <summary>按一串键。</summary>
    private static void Type(VimEngine engine, params string[] keys)
    {
        foreach (var key in keys)
        {
            engine.HandleKey(Key(key));
        }
    }

    [Fact]
    public void DeleteWord_DeletesExclusiveRange()
    {
        // dw：exclusive — 删 [光标, 下一词首) 含中间空白（operators.ts:42-54 → getOperatorRange 无扩端 → applyOperator）
        var engine = Normal("hello world", 0);
        Type(engine, "d", "w"); // w target=6（nextVimWord 跳词串+空白段）→ 删 [0,6)="hello "
        Assert.Equal("world", engine.Text);
        Assert.Equal(0, engine.Cursor);
        Assert.Equal("hello ", engine.Register.Content); // 寄存器含尾随空格（排他范围到下一词首为止）
        Assert.False(engine.Register.IsLinewise);
    }

    [Fact]
    public void DeleteWordEnd_InclusiveRange()
    {
        // de：inclusive — 右端扩一 grapheme（operators.ts:465-467）
        var engine = Normal("hello world", 0);
        Type(engine, "d", "e");
        Assert.Equal(" world", engine.Text);
        Assert.Equal("hello", engine.Register.Content);
    }

    [Fact]
    public void DeleteToLineEnd_Inclusive()
    {
        // d$：光标 3（"l"）→ 删 [3,11)="lo world" → "hel"，光标钳 'l'（maxOff）
        var engine = Normal("hello world", 3);
        Type(engine, "d", "$");
        Assert.Equal("hel", engine.Text);
        Assert.Equal(2, engine.Cursor);
    }

    [Fact]
    public void DeleteToLineStart_Exclusive()
    {
        // d^：exclusive — 删 [行首非空白, 光标)（operators.ts:42-54，^ 非 inclusive/linewise 不扩端）
        // 注：d0 在基准不可达 — fromOperator 的 /[0-9]/ 含 '0'（transitions.ts:293）→ '0' 进 operatorCount
        // 而非 motion；用 d^ 表达同语义（无缩进行首 = 行首）
        var engine = Normal("hello world", 3);
        Type(engine, "d", "^"); // ^ target=0 → 删 [0,3)="hel"
        Assert.Equal("lo world", engine.Text);
        Assert.Equal(0, engine.Cursor);
    }

    [Fact]
    public void DeleteBackward()
    {
        // db：删 [0,6)="hello " → "world"
        var engine = Normal("hello world", 6);
        Type(engine, "d", "b");
        Assert.Equal("world", engine.Text);
        Assert.Equal(0, engine.Cursor);
    }

    [Fact]
    public void ChangeWord_SpecialCaseStopsAtWordEnd()
    {
        // cw 特例：到词尾而非下一词首（operators.ts:441-450）
        var engine = Normal("hello world", 0);
        Type(engine, "c", "w");
        Assert.Equal(" world", engine.Text);
        Assert.Equal(VimMode.Insert, engine.Mode);
        Assert.Equal(0, engine.Cursor);
    }

    [Fact]
    public void ChangeWordWithCount_SpansCountMinusOneWords()
    {
        // 2cw：前进 (count-1) 词后取词尾 → 删两词
        var engine = Normal("hello world", 0);
        Type(engine, "2", "c", "w");
        Assert.Equal(string.Empty, engine.Text);
        Assert.Equal(VimMode.Insert, engine.Mode);
    }

    [Fact]
    public void ChangeToLineEnd_EntersInsert()
    {
        // C（change $，count 固定 1 — 基准 transitions.ts:141）
        var engine = Normal("hello world", 2);
        Type(engine, "C");
        Assert.Equal("he", engine.Text);
        Assert.Equal(VimMode.Insert, engine.Mode);
        Assert.Equal(2, engine.Cursor);
    }

    [Fact]
    public void DeleteToEnd_Key_D_CountIgnored()
    {
        // D：count 固定 1（基准 transitions.ts:138 传 1）— 2D 与 D 同效
        var engine = Normal("hello world", 2);
        Type(engine, "2", "D");
        Assert.Equal("he", engine.Text);
        Assert.Equal(1, engine.Cursor);
    }

    [Fact]
    public void DeleteLine_Operations()
    {
        // dd：删当前行 + 尾随 \n（operators.ts:102-166）
        var engine = Normal("hello\nworld", 0);
        Type(engine, "d", "d");
        Assert.Equal("world", engine.Text);
        Assert.Equal(0, engine.Cursor);
        Assert.Equal("hello\n", engine.Register.Content);
        Assert.True(engine.Register.IsLinewise);
    }

    [Fact]
    public void DeleteLine_LastLineAbsorbsPrecedingNewline()
    {
        // 末行删除含前导 \n（operators.ts:133-141）→ 不留尾随空行，光标钳新末行行尾
        var engine = Normal("hello\nworld", 6);
        Type(engine, "d", "d");
        Assert.Equal("hello", engine.Text);
        Assert.Equal(4, engine.Cursor); // maxOff = 5-1
        Assert.Equal("world\n", engine.Register.Content);
    }

    [Fact]
    public void DeleteLine_WithCount()
    {
        // 3dd：删 3 行
        var engine = Normal("a\nb\nc\nd", 0);
        Type(engine, "3", "d", "d");
        Assert.Equal("d", engine.Text);
    }

    [Fact]
    public void DeleteLineDown_LinewiseExtends()
    {
        // dj：当前+下一行（linewise 整行扩展 — operators.ts:451-464）
        var engine = Normal("a\nb\nc", 0);
        Type(engine, "d", "j");
        Assert.Equal("c", engine.Text);
        Assert.Equal("a\nb\n", engine.Register.Content);
    }

    [Fact]
    public void DeleteLineUp_LinewiseExtendsWithNewlineAbsorption()
    {
        // dk：当前+上一行 — 到文末时 from 收缩前导 \n
        var engine = Normal("a\nb\nc", 4);
        Type(engine, "d", "k");
        Assert.Equal("a", engine.Text);
        Assert.Equal("\nb\nc\n", engine.Register.Content);
    }

    [Fact]
    public void YankLine_MovesCursorToLineStart_TextUnchanged()
    {
        // yy：文本不变，光标到行首，寄存器行级（operators.ts:127-128）
        var engine = Normal("hello\nworld", 6);
        Type(engine, "y", "y");
        Assert.Equal("hello\nworld", engine.Text);
        Assert.Equal(6, engine.Cursor);
        Assert.Equal("world\n", engine.Register.Content);
        Assert.True(engine.Register.IsLinewise);
    }

    [Fact]
    public void YankLine_Key_Y_IsLineOp()
    {
        // Y = yy 语义（基准 transitions.ts:144 — 非 vim 原生 y$）
        var engine = Normal("hello\nworld", 6);
        engine.HandleKey(Key("Y"));
        Assert.Equal("world\n", engine.Register.Content);
        Assert.Equal("hello\nworld", engine.Text);
    }

    [Fact]
    public void ChangeLine_SingleLineClearsAndInserts()
    {
        // cc 单行：清空进 INSERT（operators.ts:152-154）
        var engine = Normal("hello", 2);
        Type(engine, "c", "c");
        Assert.Equal(string.Empty, engine.Text);
        Assert.Equal(VimMode.Insert, engine.Mode);
        Assert.Equal("hello\n", engine.Register.Content);
    }

    [Fact]
    public void ChangeLine_MultiLineCollapsesToEmptyLine()
    {
        // cc 多行：删受影响行折叠为单空行进 INSERT（operators.ts:155-162）
        // 行 0 "a" 折叠为空行 → before=[] + [""] + after=["b","c"] → "\nb\nc"
        var engine = Normal("a\nb\nc", 1);
        Type(engine, "c", "c");
        Assert.Equal("\nb\nc", engine.Text);
        Assert.Equal(VimMode.Insert, engine.Mode);
        Assert.Equal(0, engine.Cursor); // 折叠空行行首
    }

    [Fact]
    public void DeleteChar_Key_X()
    {
        // x：删光标处 1 grapheme（operators.ts:171-194）
        var engine = Normal("hello", 2);
        engine.HandleKey(Key("x"));
        Assert.Equal("helo", engine.Text);
        Assert.Equal(2, engine.Cursor);
        Assert.Equal("l", engine.Register.Content);
    }

    [Fact]
    public void DeleteChar_WithCount()
    {
        // 3x：删 3 个 grapheme
        var engine = Normal("hello", 1);
        Type(engine, "3", "x");
        Assert.Equal("ho", engine.Text);
        Assert.Equal(1, engine.Cursor);
        Assert.Equal("ell", engine.Register.Content);
    }

    [Fact]
    public void DeleteChar_AtEnd_NoOp()
    {
        var engine = Normal("hello", 5);
        engine.HandleKey(Key("x"));
        Assert.Equal("hello", engine.Text);
    }

    [Fact]
    public void DeleteChar_EmojiWholeGrapheme()
    {
        // grapheme 语义：emoji 整块删
        var engine = Normal("a👍b", 1);
        engine.HandleKey(Key("x"));
        Assert.Equal("ab", engine.Text);
    }

    [Fact]
    public void ReplaceChar()
    {
        // r{char}：替光标处字符，光标停在替换字符上（operators.ts:199-217）
        var engine = Normal("hello", 1);
        Type(engine, "r", "X");
        Assert.Equal("hXllo", engine.Text);
        Assert.Equal(1, engine.Cursor);
    }

    [Fact]
    public void ReplaceChar_WithCount()
    {
        // 3rz：替 3 个字符
        var engine = Normal("hello", 1);
        Type(engine, "3", "r", "z");
        Assert.Equal("hzzzo", engine.Text);
        Assert.Equal(3, engine.Cursor);
    }

    [Fact]
    public void ReplaceChar_CancelledByBackspace()
    {
        // r 后 Backspace（空载荷）取消 — 基准 transitions.ts:444-447
        var engine = Normal("hello", 1);
        Type(engine, "r");
        engine.HandleKey(KeyPress.Named(KeyKind.Backspace));
        Assert.Equal("hello", engine.Text);
        Assert.Equal(VimCommandKind.Idle, engine.Command.Kind);
    }

    [Fact]
    public void ToggleCase()
    {
        // ~：翻转光标处字符，光标移到其后（operators.ts:222-253）
        var engine = Normal("hello", 0);
        engine.HandleKey(Key("~"));
        Assert.Equal("Hello", engine.Text);
        Assert.Equal(1, engine.Cursor);
    }

    [Fact]
    public void ToggleCase_UpperToLower()
    {
        var engine = Normal("HELLO", 0);
        engine.HandleKey(Key("~"));
        Assert.Equal("hELLO", engine.Text);
    }

    [Fact]
    public void ToggleCase_WithCount()
    {
        var engine = Normal("hello", 0);
        Type(engine, "3", "~");
        Assert.Equal("HELlo", engine.Text);
        Assert.Equal(3, engine.Cursor);
    }

    [Fact]
    public void JoinLines()
    {
        // J：后续行去前导空白 + 单空格衔接，光标停衔接处（operators.ts:258-289）
        var engine = Normal("hello\nworld", 5);
        engine.HandleKey(Key("J"));
        Assert.Equal("hello world", engine.Text);
        Assert.Equal(5, engine.Cursor);
    }

    [Fact]
    public void JoinLines_TrimsLeadingWhitespaceOfNextLine()
    {
        var engine = Normal("a\n   b\nc", 0);
        engine.HandleKey(Key("J"));
        Assert.Equal("a b\nc", engine.Text);
    }

    [Fact]
    public void JoinLines_LastLineNoOp()
    {
        var engine = Normal("a\nb", 2);
        engine.HandleKey(Key("J"));
        Assert.Equal("a\nb", engine.Text);
    }

    [Fact]
    public void JoinLines_WithCount()
    {
        // 2J：合并 2 个后续行
        var engine = Normal("a\nb\nc\nd", 0);
        Type(engine, "2", "J");
        Assert.Equal("a b c\nd", engine.Text);
    }

    [Fact]
    public void PasteLinewise_Below()
    {
        // 行级 p：当前行下插入，光标到插入首行行首（operators.ts:305-325）
        var engine = Normal("x\ny", 0);
        Type(engine, "y", "y"); // 寄存器 "x\n"
        engine.HandleKey(Key("p"));
        Assert.Equal("x\nx\ny", engine.Text);
        Assert.Equal(2, engine.Cursor);
    }

    [Fact]
    public void PasteLinewise_Before()
    {
        // 行级 P：当前行前插入（operators.ts:305-325 insertLine = currentLine）
        var engine = Normal("x\ny", 0); // 行 0 "x" 首（原 offset 2 是行 1 "y" — yy 抽的是 y 行，场景笔误修正）
        Type(engine, "y", "y"); // register "x\n"
        engine.HandleKey(Key("P"));
        Assert.Equal("x\nx\ny", engine.Text);
        Assert.Equal(0, engine.Cursor); // 插入首行行首
    }

    [Fact]
    public void PasteCharwise_InsertAfterCursorGrapheme()
    {
        // 字符级 p：光标右邻 grapheme 后插入，光标落内容末字符上（operators.ts:326-342）
        var engine = Normal("hello", 2);
        Type(engine, "d", "w"); // 删 [2,5)="llo" → "he"，寄存器 "llo"，光标钳 maxOff=1
        Assert.Equal(1, engine.Cursor);
        engine.HandleKey(Key("p")); // after：offset 1 < len 2 → insertPoint = Next(1) = 2
        Assert.Equal("hello", engine.Text); // "he"+"llo" 还原
        Assert.Equal(4, engine.Cursor); // insertPoint + len - lastGrapheme = 2+3-1
    }

    [Fact]
    public void PasteCharwise_Before()
    {
        var engine = Normal("hello", 2);
        Type(engine, "d", "w"); // "he" + register "llo"，光标 1
        engine.HandleKey(Key("P")); // before：insertPoint = offset = 1
        Assert.Equal("hlloe", engine.Text);
        Assert.Equal(3, engine.Cursor); // 1+3-1（内容末字符 'o' 上）
    }

    [Fact]
    public void PasteWithCount_Repeats()
    {
        var engine = Normal("x", 0);
        Type(engine, "d", "w"); // 删 "x" → ""，寄存器 "x"，光标 0
        Type(engine, "2", "p");
        Assert.Equal("xx", engine.Text);
    }

    [Fact]
    public void PasteEmptyRegister_NoOp()
    {
        var engine = Normal("hello", 0);
        engine.HandleKey(Key("p"));
        Assert.Equal("hello", engine.Text);
    }

    [Fact]
    public void OpenLine_Below()
    {
        // o：当前行下开空行进 INSERT（operators.ts:397-416）
        var engine = Normal("a\nb", 1);
        engine.HandleKey(Key("o"));
        Assert.Equal("a\n\nb", engine.Text);
        Assert.Equal(VimMode.Insert, engine.Mode);
        Assert.Equal(2, engine.Cursor);
    }

    [Fact]
    public void OpenLine_Above()
    {
        var engine = Normal("a\nb", 1);
        engine.HandleKey(Key("O"));
        Assert.Equal("\na\nb", engine.Text);
        Assert.Equal(VimMode.Insert, engine.Mode);
        Assert.Equal(0, engine.Cursor);
    }

    [Fact]
    public void IndentIncrease()
    {
        // >>：行首加两空格，光标到首个非空白（operators.ts:348-392）
        var engine = Normal("  a\nb", 0);
        Type(engine, ">", ">");
        Assert.Equal("    a\nb", engine.Text);
        Assert.Equal(4, engine.Cursor);
    }

    [Fact]
    public void IndentDecrease_TwoSpaces()
    {
        var engine = Normal("  a\nb", 4); // 行 1 "b"
        Type(engine, "<", "<");
        Assert.Equal("  a\nb", engine.Text); // "b" 无前导 → 不变
        Assert.Equal(4, engine.Cursor); // 行 1 首个非空白 = 行首（偏移 4）
    }

    [Fact]
    public void IndentDecrease_Tab()
    {
        var engine = Normal("\tb", 0);
        Type(engine, "<", "<");
        Assert.Equal("b", engine.Text);
    }

    [Fact]
    public void IndentDecrease_PartialWhitespace()
    {
        // 去至多两前导空白（operators.ts:370-381）
        var engine = Normal(" b", 0);
        Type(engine, "<", "<");
        Assert.Equal("b", engine.Text);
    }

    [Fact]
    public void IndentWithCount()
    {
        var engine = Normal("a\nb\nc", 0);
        Type(engine, "2", ">", ">");
        Assert.Equal("  a\n  b\nc", engine.Text);
    }

    [Fact]
    public void IndentCancelledByWrongKey()
    {
        // > 后非 > 键取消回 idle（transitions.ts:458）
        var engine = Normal("  a", 0);
        Type(engine, ">");
        engine.HandleKey(Key("x")); // x 不是 > → 取消
        Assert.Equal(VimCommandKind.Idle, engine.Command.Kind);
        Assert.Equal("  a", engine.Text); // x 未执行（indent 态吞掉）
    }

    [Fact]
    public void DeleteToBufferEnd()
    {
        // dG：删到末行（linewise — operators.ts:524-539）
        var engine = Normal("a\nb\nc", 2); // 行 1 "b" 首
        Type(engine, "d", "G");
        Assert.Equal("a", engine.Text);
        Assert.Equal("\nb\nc\n", engine.Register.Content);
        Assert.True(engine.Register.IsLinewise);
    }

    [Fact]
    public void DeleteToFirstLine()
    {
        // dgg：删到首行（operators.ts:541-556）
        var engine = Normal("a\nb\nc", 4); // 行 2 "c" 首
        Type(engine, "d", "g", "g");
        Assert.Equal(string.Empty, engine.Text);
    }

    [Fact]
    public void GoToLine_WithCount_OperatorVariant()
    {
        // 2dG：删到第 2 行
        var engine = Normal("a\nb\nc\nd", 0);
        Type(engine, "2", "d", "G");
        Assert.Equal("c\nd", engine.Text);
    }

    [Fact]
    public void DeleteToFind_Inclusive()
    {
        // dfx：find 一律 inclusive（operators.ts:479-491）— 含目标 x 整个 grapheme：
        // 删 [0, NextGraphemeStart(6)) = "hello x"，剩 "yz"；寄存器同步含 x
        var engine = Normal("hello xyz", 0);
        Type(engine, "d", "f", "x");
        Assert.Equal("yz", engine.Text);
        Assert.Equal("hello x", engine.Register.Content);
    }

    [Fact]
    public void DeleteToFind_Miss_NoOp()
    {
        var engine = Normal("hello", 0);
        Type(engine, "d", "f", "z");
        Assert.Equal("hello", engine.Text);
        Assert.Equal(VimCommandKind.Idle, engine.Command.Kind);
    }

    [Fact]
    public void DeleteInnerWord()
    {
        // diw（operators.ts:80-97 executeOperatorTextObj）
        var engine = Normal("hello world", 2);
        Type(engine, "d", "i", "w");
        Assert.Equal(" world", engine.Text);
        Assert.Equal("hello", engine.Register.Content);
    }

    [Fact]
    public void DeleteAroundWord()
    {
        // daw：含尾随空白
        var engine = Normal("hello world", 2);
        Type(engine, "d", "a", "w");
        Assert.Equal("world", engine.Text);
        Assert.Equal("hello ", engine.Register.Content);
    }

    [Fact]
    public void ChangeInnerParentheses()
    {
        // ci(：删括号内部进 INSERT
        var engine = Normal("f(abc)g", 4);
        Type(engine, "c", "i", "(");
        Assert.Equal("f()g", engine.Text);
        Assert.Equal(VimMode.Insert, engine.Mode);
        Assert.Equal(2, engine.Cursor);
    }

    [Fact]
    public void YankInnerQuotes()
    {
        // yi"：光标不动文本不变，寄存器收内容，光标到范围起点（yank → setOffset(from)）
        var engine = Normal("say \"hi\" ok", 6);
        Type(engine, "y", "i", "\"");
        Assert.Equal("say \"hi\" ok", engine.Text);
        Assert.Equal("hi", engine.Register.Content);
        Assert.Equal(5, engine.Cursor);
    }

    [Fact]
    public void YankWord_CursorMovesToRangeStart()
    {
        // yw：光标到范围起点（operators.ts:507-508）
        var engine = Normal("hello world", 6);
        Type(engine, "y", "w");
        Assert.Equal("hello world", engine.Text);
        Assert.Equal("world", engine.Register.Content);
        Assert.Equal(6, engine.Cursor);
    }

    [Fact]
    public void DeleteOnEmptyText_NoOp()
    {
        var engine = Normal(string.Empty, 0);
        Type(engine, "d", "d");
        Assert.Equal(string.Empty, engine.Text);
        engine.HandleKey(Key("x"));
        Assert.Equal(string.Empty, engine.Text);
    }

    [Fact]
    public void OperatorWithMotionCount_Multiplied()
    {
        // 2d3w = 6 词（基准 fromOperatorCount — transitions.ts:327）
        var engine = Normal("a b c d e f g h i j k l m", 0);
        Type(engine, "2", "d", "3", "w");
        // 6 词 = "a b c d e f "（6 个词 + 5 空格）→ 剩 "g h ..."
        Assert.Equal("g h i j k l m", engine.Text);
    }
}
