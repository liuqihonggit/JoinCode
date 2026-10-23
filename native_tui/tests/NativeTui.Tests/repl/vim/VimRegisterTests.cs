namespace NativeTui.Tests.Repl.Vim;

/// <summary>
/// vim 寄存器 yank-paste 往返测试 — 基准 src/vim/types.ts:81-86 PersistentState.register
/// + operators.ts setRegister/executePaste 数据通道：
/// charwise 往返（yw→p/P）、linewise 往返（yy/dd→p/P）、count 重复、linewise 判定（内容尾 \n）。
/// </summary>
public sealed class VimRegisterTests
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
    public void Register_InitialStateEmpty()
    {
        // 基准 createInitialPersistentState：register = ''（types.ts:192-198）
        var engine = new VimEngine();
        Assert.True(engine.Register.IsEmpty);
        Assert.False(engine.Register.IsLinewise);
        Assert.False(engine.Register.EndsLinewise);
    }

    [Fact]
    public void YankWord_Paste_RoundTripCharwise()
    {
        // yw → p：字符级粘贴在光标右邻 grapheme 后，光标落内容末字符上
        var engine = Normal("hello world", 6);
        Type(engine, "y", "w"); // register "world"，光标回 from=6
        engine.HandleKey(Key("p")); // after：offset 6 < 11 → insertPoint = 7
        Assert.Equal("hello wworldorld", engine.Text); // text[..7] + "world" + text[7..]
        Assert.Equal(11, engine.Cursor); // 7+5-1（'d' 上）
    }

    [Fact]
    public void YankWord_PasteBefore_RoundTrip()
    {
        var engine = Normal("hello world", 6);
        Type(engine, "y", "w");
        engine.HandleKey(Key("P")); // before：insertPoint = 6
        Assert.Equal("hello worldworld", engine.Text);
        Assert.Equal(10, engine.Cursor); // 6+5-1
    }

    [Fact]
    public void YankLine_Paste_RoundTripLinewise()
    {
        // yy → p：行级粘贴在当前行下，光标到插入首行行首（operators.ts:305-325）
        var engine = Normal("a\nb\nc", 0);
        Type(engine, "y", "y"); // register "a\n"
        engine.HandleKey(Key("p"));
        Assert.Equal("a\na\nb\nc", engine.Text);
        Assert.Equal(2, engine.Cursor);
        Assert.True(engine.Register.IsLinewise);
    }

    [Fact]
    public void YankLine_PasteBefore_ThenYankAgain()
    {
        // yy → P：行级粘贴在当前行上
        var engine = Normal("a\nb\nc", 0);
        Type(engine, "y", "y");
        engine.HandleKey(Key("P"));
        Assert.Equal("a\na\nb\nc", engine.Text);
        Assert.Equal(0, engine.Cursor);
    }

    [Fact]
    public void DeleteLine_PasteBelow_InsertsAfterCurrentLine()
    {
        // dd → p：行级粘贴在光标行下 — 删行 1 后光标落在行 1（原行 2），p 在其后
        var engine = Normal("a\nb\nc", 2); // 行 1 "b" 首
        Type(engine, "d", "d"); // register "b\n"，文本 "a\nc"，光标 2
        engine.HandleKey(Key("p")); // 当前行（"c" 行）下插入
        Assert.Equal("a\nc\nb", engine.Text);
        Assert.Equal(4, engine.Cursor); // 插入行行首
    }

    [Fact]
    public void DeleteLine_PasteBefore_RestoresOriginalPosition()
    {
        // dd → P：当前行前插入 → 往复原样
        var engine = Normal("a\nb\nc", 2);
        Type(engine, "d", "d");
        engine.HandleKey(Key("P"));
        Assert.Equal("a\nb\nc", engine.Text);
        Assert.Equal(2, engine.Cursor);
    }

    [Fact]
    public void DeleteLine_LastLinePaste_RestoresWithoutTrailingBlank()
    {
        // 末行 dd 收前导 \n（operators.ts:133-141）→ 粘贴回原位形态
        var engine = Normal("a\nb", 2);
        Type(engine, "d", "d"); // register "b\n"，文本 "a"，光标 1
        Assert.Equal("a", engine.Text);
        engine.HandleKey(Key("p")); // 行 1 插入
        Assert.Equal("a\nb", engine.Text);
    }

    [Fact]
    public void CrossLineCharwiseDelete_RegisterContentEndsWithNewline()
    {
        // dw 跨行删（含 \n）→ 寄存器内容尾随 \n — EndsLinewise 判定与 IsLinewise 记录字段分离
        //（基准粘贴分流按 register.endsWith('\n')，operators.ts:302 — 字段仅记录）
        var engine = Normal("ab\ncd", 0);
        Type(engine, "d", "w"); // w 跨 \n → 删 [0,3)="ab\n"
        Assert.Equal("cd", engine.Text);
        Assert.False(engine.Register.IsLinewise); // 写入时标记 charwise
        Assert.True(engine.Register.EndsLinewise); // 但内容尾随 \n → 粘贴按 linewise 分流
        engine.Sync("ab\ncd", 0); // 恢复文本（宿主 undo 模拟）
        Type(engine, "y", "y"); // register "ab\n"（linewise 覆盖）
        Assert.Equal("ab\n", engine.Register.Content);
        Assert.True(engine.Register.EndsLinewise);
        Assert.True(engine.Register.IsLinewise);
    }

    [Fact]
    public void LinewiseDetection_ByTrailingNewline()
    {
        // 粘贴分流按内容尾 \n（operators.ts:302）— IsLinewise 字段仅记录
        var register = new VimRegister();
        register.Write("abc", linewise: false);
        Assert.False(register.EndsLinewise);
        register.Write("abc\n", linewise: true);
        Assert.True(register.EndsLinewise);
    }

    [Fact]
    public void PasteWithCount_LinewiseRepeatsLines()
    {
        var engine = Normal("a\nb", 0);
        Type(engine, "y", "y"); // "a\n"
        Type(engine, "2", "p");
        Assert.Equal("a\na\na\nb", engine.Text);
        Assert.Equal(2, engine.Cursor);
    }

    [Fact]
    public void PasteWithCount_CharwiseRepeatsContent()
    {
        var engine = Normal("x", 0);
        Type(engine, "y", "w"); // "x"
        Type(engine, "3", "p");
        Assert.Equal("xxxx", engine.Text); // "x" + "xxx"（after：文末 insertPoint=0? offset0 < 1 → next=1）
        Assert.Equal(3, engine.Cursor); // 1+3-1
    }

    [Fact]
    public void EmptyRegister_PasteIsNoOp()
    {
        // 空寄存器粘贴无操作（operators.ts:299-300）
        var engine = Normal("abc", 1);
        engine.HandleKey(Key("p"));
        engine.HandleKey(Key("P"));
        Assert.Equal("abc", engine.Text);
        Assert.Equal(1, engine.Cursor);
    }

    [Fact]
    public void CharwisePaste_InnerOfLine_PastesAtCursorGraphemeBoundary()
    {
        // after 插入点 = 光标右邻 grapheme 终点（operators.ts:329-331）
        var engine = Normal("a👍b", 1);
        Type(engine, "x"); // 删 emoji（整 grapheme）→ "ab"，register "👍"，光标 1
        engine.HandleKey(Key("p")); // after：offset 1 < 2 → insertPoint = 2
        Assert.Equal("ab👍", engine.Text);
        // 光标 = insertPoint + len - lastGrapheme.length（operators.ts:337-341）= 2+2-2 = 2 — emoji 占 2 char（lastGrapheme 语义）
        Assert.Equal(2, engine.Cursor);
    }
}
