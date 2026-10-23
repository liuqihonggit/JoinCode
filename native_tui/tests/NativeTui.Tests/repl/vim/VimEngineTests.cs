namespace NativeTui.Tests.Repl.Vim;

/// <summary>
/// vim 引擎状态机测试 — 基准 src/hooks/useVimInput.ts（模式切换/INSERT 透传/键映射/insertedText 跟踪）
/// + src/vim/transitions.ts（11 态转移/count 前缀/pending 取消/dot-repeat/;/, 重复查找）。
/// </summary>
public sealed class VimEngineTests
{
    /// <summary>NORMAL 模式引擎工厂。</summary>
    private static VimEngine Normal(string text, int offset) =>
        VimEngine.Of(text, offset, VimMode.Normal);

    /// <summary>INSERT 模式引擎工厂。</summary>
    private static VimEngine Insert(string text, int offset) =>
        VimEngine.Of(text, offset, VimMode.Insert);

    /// <summary>单字符按键快捷。</summary>
    private static KeyPress Key(string input) => KeyPress.Char(input);

    /// <summary>按一串字符键。</summary>
    private static void Type(VimEngine engine, params string[] keys)
    {
        foreach (var key in keys)
        {
            engine.HandleKey(Key(key));
        }
    }

    [Fact]
    public void InitialMode_IsInsert()
    {
        // 基准 createInitialVimState 返回 INSERT（types.ts:188-190）
        var engine = new VimEngine();
        Assert.Equal(VimMode.Insert, engine.Mode);
        Assert.Equal("-- INSERT --", engine.StatusLine);
    }

    [Fact]
    public void StatusLine_NormalEmpty()
    {
        // 基准仅 INSERT 显示 -- INSERT --（PromptInputFooterLeftSide.tsx:191）
        var engine = Normal("abc", 0);
        Assert.Equal(string.Empty, engine.StatusLine);
    }

    [Fact]
    public void Escape_FromInsert_SwitchesToNormalAndMovesCursorLeft()
    {
        // 基准 useVimInput.ts:61-80：退出 INSERT 光标左移一格
        var engine = Insert("hello", 3);
        var effects = engine.HandleKey(KeyPress.Named(KeyKind.Escape));
        Assert.Equal(VimMode.Normal, engine.Mode);
        Assert.Equal(2, engine.Cursor);
        Assert.Contains(effects, e => e.Kind == VimEffectKind.MoveCursor && e.Offset == 2);
    }

    [Fact]
    public void Escape_FromInsert_AtOffsetZero_NoMove()
    {
        var engine = Insert("hello", 0);
        engine.HandleKey(KeyPress.Named(KeyKind.Escape));
        Assert.Equal(VimMode.Normal, engine.Mode);
        Assert.Equal(0, engine.Cursor);
    }

    [Fact]
    public void Escape_FromInsert_AtLineStart_NoMove()
    {
        // 前一字符是 \n（行首）不左移（基准 props.value[offset-1] !== '\n' 守卫）
        var engine = Insert("ab\ncd", 3);
        engine.HandleKey(KeyPress.Named(KeyKind.Escape));
        Assert.Equal(3, engine.Cursor);
    }

    [Fact]
    public void Escape_FromInsert_RecordsInsertDotRepeat()
    {
        // 键入文本非空 → lastChange 记 insert（useVimInput.ts:63-68）
        var engine = Insert(string.Empty, 0);
        engine.HandleKey(Key("a"));
        engine.HandleKey(Key("b"));
        engine.Sync("ab", 2); // 宿主回写镜像
        engine.HandleKey(KeyPress.Named(KeyKind.Escape));
        Assert.Equal(VimMode.Normal, engine.Mode);
        Assert.NotNull(engine.LastChange);
        Assert.Equal(VimRecordedChangeKind.Insert, engine.LastChange.Kind);
        Assert.Equal("ab", engine.LastChange.Text);
    }

    [Fact]
    public void Escape_FromNormal_CancelsPendingCommand()
    {
        // NORMAL Esc 取消 pending（useVimInput.ts:198-201）
        var engine = Normal("hello", 0);
        Type(engine, "d");
        Assert.Equal(VimCommandKind.Operator, engine.Command.Kind);
        engine.HandleKey(KeyPress.Named(KeyKind.Escape));
        Assert.Equal(VimCommandKind.Idle, engine.Command.Kind);
        Assert.Equal("hello", engine.Text);
    }

    [Fact]
    public void ModeCycle_InsertNormalInsert()
    {
        // i → 字符 → Esc → i 循环（模式状态机往返）
        var engine = Normal("ab", 0);
        var effects = engine.HandleKey(Key("i"));
        Assert.Equal(VimMode.Insert, engine.Mode);
        Assert.Contains(effects, e => e.Kind == VimEffectKind.EnterInsert && e.Offset == 0);

        engine.HandleKey(Key("x")); // INSERT 透传（宿主编辑）
        engine.Sync("axb", 2); // 宿主回写
        Assert.Equal("axb", engine.Text);

        engine.HandleKey(KeyPress.Named(KeyKind.Escape));
        Assert.Equal(VimMode.Normal, engine.Mode);

        engine.HandleKey(Key("i"));
        Assert.Equal(VimMode.Insert, engine.Mode);
    }

    [Fact]
    public void InsertMode_PassesThroughKeys()
    {
        // INSERT 所有非 Esc/Enter/Ctrl 键透传（useVimInput.ts:209-229）
        var engine = Insert("abc", 1);
        var effects = engine.HandleKey(Key("X"));
        var effect = Assert.Single(effects);
        Assert.Equal(VimEffectKind.PassThrough, effect.Kind);
        Assert.Equal("X", effect.Key!.Input);
        Assert.Equal("X", engine.InsertedText);
    }

    [Fact]
    public void InsertMode_TracksInsertedText_BackspaceStripsGrapheme()
    {
        // Backspace 剥末 grapheme（useVimInput.ts:211-218）— emoji 整块剥
        var engine = Insert(string.Empty, 0);
        engine.HandleKey(Key("a"));
        engine.HandleKey(KeyPress.Char("👍")); // 多字符输入载荷（粘贴/IME 形态）
        Assert.Equal("a👍", engine.InsertedText);
        engine.HandleKey(KeyPress.Named(KeyKind.Backspace));
        Assert.Equal("a", engine.InsertedText);
        engine.HandleKey(KeyPress.Named(KeyKind.Delete));
        Assert.Equal(string.Empty, engine.InsertedText);
    }

    [Fact]
    public void CtrlKeys_PassThroughInBothModes()
    {
        // Ctrl 两模式透传（useVimInput.ts:184-187）
        var insert = Insert("abc", 0);
        var insertEffects = insert.HandleKey(KeyPress.Named(KeyKind.Char, ctrl: true));
        Assert.Equal(VimEffectKind.PassThrough, Assert.Single(insertEffects).Kind);

        var normal = Normal("abc", 0);
        var normalEffects = normal.HandleKey(KeyPress.Named(KeyKind.Char, ctrl: true));
        Assert.Equal(VimEffectKind.PassThrough, Assert.Single(normalEffects).Kind);
        Assert.Equal(VimMode.Normal, normal.Mode);
    }

    [Fact]
    public void Enter_PassesThroughInBothModes()
    {
        // Enter 允许 NORMAL 直接提交（useVimInput.ts:204-207）
        var normal = Normal("abc", 0);
        var effects = normal.HandleKey(KeyPress.Named(KeyKind.Return));
        Assert.Equal(VimEffectKind.PassThrough, Assert.Single(effects).Kind);
        Assert.Equal(VimMode.Normal, normal.Mode);
    }

    [Fact]
    public void NormalIdle_ArrowKeys_PassThroughForHistory()
    {
        // idle 方向键委托宿主历史导航（useVimInput.ts:235-243）
        var engine = Normal("abc", 1);
        foreach (var kind in new[] { KeyKind.UpArrow, KeyKind.DownArrow, KeyKind.LeftArrow, KeyKind.RightArrow })
        {
            var effects = engine.HandleKey(KeyPress.Named(kind));
            Assert.Equal(VimEffectKind.PassThrough, Assert.Single(effects).Kind);
        }
    }

    [Fact]
    public void NormalPendingOperator_ArrowKeys_MapToMotions()
    {
        // 非 idle 方向键映射 motion（useVimInput.ts:264-268）— d + ↓ = dj（删当前+下一行）
        var engine = Normal("abc\ndef\nghi", 0);
        Type(engine, "d"); // operator 待定
        var effects = engine.HandleKey(KeyPress.Named(KeyKind.DownArrow)); // j → dj
        Assert.Equal("ghi", engine.Text);
        Assert.DoesNotContain(effects, e => e.Kind == VimEffectKind.PassThrough);
    }

    [Fact]
    public void NormalBackspace_MapsToCharLeft()
    {
        // idle Backspace → h（useVimInput.ts:269）
        var engine = Normal("abc", 2);
        var effects = engine.HandleKey(KeyPress.Named(KeyKind.Backspace));
        Assert.Equal(1, engine.Cursor);
        Assert.Contains(effects, e => e.Kind == VimEffectKind.MoveCursor);
    }

    [Fact]
    public void NormalDelete_MapsToX()
    {
        // idle Delete → x（useVimInput.ts:270-271）
        var engine = Normal("abc", 1);
        var effects = engine.HandleKey(KeyPress.Named(KeyKind.Delete));
        Assert.Equal("ac", engine.Text);
        Assert.Contains(effects, e => e.Kind == VimEffectKind.ApplyText);
    }

    [Fact]
    public void CountState_Delete_Key_IsNotDestructive()
    {
        // count 态 Delete 不执行破坏性 Nx（useVimInput.ts:270 条件 type !== 'count'）→ 空输入回 idle
        var engine = Normal("abc", 0);
        Type(engine, "2");
        Assert.Equal(VimCommandKind.Count, engine.Command.Kind);
        engine.HandleKey(KeyPress.Named(KeyKind.Delete));
        Assert.Equal("abc", engine.Text);
        Assert.Equal(VimCommandKind.Idle, engine.Command.Kind);
    }

    [Fact]
    public void CountPrefix_AccumulatesDigits()
    {
        // 数字累计 + 10w（transitions.ts:270-274）
        var engine = Normal("a b c d e f g h i j", 0);
        Type(engine, "1", "0", "w");
        Assert.Equal(19, engine.Cursor); // 10 词 → 文末（钳制）
        Assert.Equal(VimCommandKind.Idle, engine.Command.Kind);
    }

    [Fact]
    public void CountPrefix_CapsAtMaxCount()
    {
        // MAX_VIM_COUNT=10000 钳制（types.ts:182）
        var engine = Normal("ab", 0);
        Type(engine, "9", "9", "9", "9", "9", "9", "9");
        Assert.Equal("10000", engine.Command.Digits); // 钳制后回写 digits
    }

    [Fact]
    public void ZeroKey_IdleIsLineStart_InCountIsDigit()
    {
        // idle '0' 是行首 motion（transitions.ts:253-257）；count 态 '0' 是数字
        var engine = Normal("  ab", 3);
        engine.HandleKey(Key("0"));
        Assert.Equal(0, engine.Cursor);

        var engineCount = Normal("ab", 0);
        Type(engineCount, "1", "0", "w");
        Assert.Equal(2, engineCount.Cursor); // 10w → 文末
    }

    [Fact]
    public void PendingOperator_AfterDigit_EntersOperatorCount()
    {
        // d 后数字 → operatorCount（transitions.ts:293-302）
        var engine = Normal("a b c", 0);
        Type(engine, "d", "2");
        Assert.Equal(VimCommandKind.OperatorCount, engine.Command.Kind);
        Assert.Equal("2", engine.Command.Digits);
        engine.HandleKey(Key("w"));
        Assert.Equal("c", engine.Text); // d2w = 删 2 词
    }

    [Fact]
    public void InvalidKey_InIdle_Swallowed()
    {
        // 无效键吞掉保持 idle（transitions.ts:262 返回 {}）
        var engine = Normal("abc", 0);
        var effects = engine.HandleKey(Key("z"));
        Assert.Empty(effects);
        Assert.Equal(VimCommandKind.Idle, engine.Command.Kind);
    }

    [Fact]
    public void InvalidKey_InCount_ReturnsToIdle()
    {
        // count 态无效键回 idle（transitions.ts:280）
        var engine = Normal("abc", 0);
        Type(engine, "5", "z");
        Assert.Equal(VimCommandKind.Idle, engine.Command.Kind);
    }

    [Fact]
    public void InvalidKey_AfterOperator_ReturnsToIdle()
    {
        // operator 态无效键回 idle（transitions.ts:307）
        var engine = Normal("abc", 0);
        Type(engine, "d", "z");
        Assert.Equal(VimCommandKind.Idle, engine.Command.Kind);
        Assert.Equal("abc", engine.Text);
    }

    [Fact]
    public void GState_gg_gj_gk()
    {
        // g 前缀状态机（transitions.ts:385-418）
        var engine = Normal("a\nb\nc", 4);
        Type(engine, "g", "g");
        Assert.Equal(0, engine.Cursor);

        engine = Normal("a\nb\nc", 2);
        Type(engine, "g", "k"); // gk 视觉行上移（单行不折 → 逻辑行）
        Assert.Equal(0, engine.Cursor);

        engine = Normal("a\nb\nc", 0);
        Type(engine, "g", "j");
        Assert.Equal(2, engine.Cursor);
    }

    [Fact]
    public void GState_WithCount_ggGoesToLine()
    {
        // 5gg → 第 5 行（transitions.ts:399-411）
        var engine = Normal("a\nb\nc\nd\ne", 0);
        Type(engine, "5", "g", "g");
        Assert.Equal(8, engine.Cursor); // "e" 行首（a0 \n1 b2 \n3 c4 \n5 d6 \n7 e8）
    }

    [Fact]
    public void GState_InvalidKey_ReturnsToIdle()
    {
        var engine = Normal("abc", 0);
        Type(engine, "g", "z");
        Assert.Equal(VimCommandKind.Idle, engine.Command.Kind);
    }

    [Fact]
    public void GKey_Idle_GoesToLastLine()
    {
        // G：末行首（transitions.ts:146-158 count=1 分支）
        var engine = Normal("a\nb\nc", 0);
        engine.HandleKey(Key("G"));
        Assert.Equal(4, engine.Cursor);
    }

    [Fact]
    public void GKey_WithCount_GoesToLineN()
    {
        var engine = Normal("a\nb\nc", 0);
        Type(engine, "2", "G");
        Assert.Equal(2, engine.Cursor);
    }

    [Fact]
    public void InsertKeys_Positions()
    {
        // i/I/a/A 定位（transitions.ts:168-191）
        var engine = Normal("  ab", 3);
        Type(engine, "I");
        Assert.Equal(VimMode.Insert, engine.Mode);
        Assert.Equal(2, engine.Cursor); // 首个非空白

        engine = Normal("  ab", 2);
        Type(engine, "a");
        Assert.Equal(3, engine.Cursor); // 右移一 grapheme

        engine = Normal("ab", 2); // 光标在文末
        Type(engine, "a");
        Assert.Equal(2, engine.Cursor); // isAtEnd 不动

        engine = Normal("ab\ncd", 1);
        Type(engine, "A");
        Assert.Equal(2, engine.Cursor); // 行尾
    }

    [Fact]
    public void UndoKey_EmitsUndoRequest()
    {
        // u → UndoRequested 效果（基准 onUndo 委托，transitions.ts:165-167）
        var engine = Normal("abc", 0);
        var effects = engine.HandleKey(Key("u"));
        Assert.Contains(effects, e => e.Kind == VimEffectKind.UndoRequested);
        Assert.Equal("abc", engine.Text); // 引擎不改文本 — 宿主 undo
    }

    [Fact]
    public void FindKey_SemicolonRepeats()
    {
        // fx → ; 重复（transitions.ts:162-163 → 465-490）
        var engine = Normal("aXbXc", 0);
        Type(engine, "f", "X");
        Assert.Equal(1, engine.Cursor);
        engine.HandleKey(Key(";"));
        Assert.Equal(3, engine.Cursor);
        engine.HandleKey(Key(";")); // 再无 → 不动
        Assert.Equal(3, engine.Cursor);
    }

    [Fact]
    public void FindKey_CommaRepeatsReverse()
    {
        // , 反向重复（flip f↔F — transitions.ts:477-484 flipMap；executeRepeatFind 465-490）
        var engine = Normal("aXbXc", 0);
        Type(engine, "f", "X"); // 正向 → 1，记 lastFind {f, X}
        Assert.Equal(1, engine.Cursor);
        engine.HandleKey(Key(";")); // 同向重复 → 3
        Assert.Equal(3, engine.Cursor);
        engine.HandleKey(Key(",")); // 翻转 F：反向从 3 → 越过 b 找到 X(1)
        Assert.Equal(1, engine.Cursor);
    }

    [Fact]
    public void DotRepeat_ReplaysOperator()
    {
        // dw. → 再删一词（useVimInput.ts:109-173 replayLastChange）
        var engine = Normal("one two three", 0);
        Type(engine, "d", "w");
        Assert.Equal("two three", engine.Text);
        engine.HandleKey(Key("."));
        Assert.Equal("three", engine.Text);
        engine.HandleKey(Key("."));
        Assert.Equal(string.Empty, engine.Text);
    }

    [Fact]
    public void DotRepeat_ReplaysInsert()
    {
        // 键入 ab → Esc → . 在光标处重插 ab
        var engine = Insert(string.Empty, 0);
        engine.HandleKey(Key("a"));
        engine.HandleKey(Key("b"));
        engine.Sync("ab", 2);
        engine.HandleKey(KeyPress.Named(KeyKind.Escape)); // 记 insert "ab"，光标左移到 1
        Assert.Equal(VimMode.Normal, engine.Mode);
        Assert.Equal(1, engine.Cursor);
        engine.HandleKey(Key(".")); // 在偏移 1 处重插 "ab" → "aabb"，光标 3
        Assert.Equal("aabb", engine.Text);
        Assert.Equal(3, engine.Cursor);
    }

    [Fact]
    public void DotRepeat_ReplaysX()
    {
        var engine = Normal("hello", 1);
        engine.HandleKey(Key("x")); // 删 'e' → "hllo"，光标 1，记 x
        engine.HandleKey(Key(".")); // 在光标 1 再删 'l'
        Assert.Equal("hlo", engine.Text);
    }

    [Fact]
    public void DotRepeat_LineOpIsNoOp_AliasesBaselineBehavior()
    {
        // dd. 是 noop — 基准 executeLineOp 记 motion: op[0]（'d'），重放 resolveMotion 不识别（operators.ts:165）
        var engine = Normal("a\nb\nc", 0);
        Type(engine, "d", "d");
        Assert.Equal("b\nc", engine.Text);
        engine.HandleKey(Key("."));
        Assert.Equal("b\nc", engine.Text); // 对齐基准：lineOp dot-repeat 无操作
    }

    [Fact]
    public void DotRepeat_NoChangeRecorded_NoOp()
    {
        var engine = Normal("abc", 0);
        engine.HandleKey(Key("."));
        Assert.Equal("abc", engine.Text);
    }

    [Fact]
    public void ReplaceState_WaitsForChar()
    {
        var engine = Normal("hello", 1);
        Type(engine, "r");
        Assert.Equal(VimCommandKind.Replace, engine.Command.Kind);
        engine.HandleKey(Key("Q"));
        Assert.Equal("hQllo", engine.Text);
        Assert.Equal(VimCommandKind.Idle, engine.Command.Kind);
    }

    [Fact]
    public void Sync_UpdatesMirror()
    {
        // 宿主回写通道
        var engine = new VimEngine();
        engine.Sync("new text", 4);
        Assert.Equal("new text", engine.Text);
        Assert.Equal(4, engine.Cursor);
        engine.Sync("ab", 99); // 偏移钳制
        Assert.Equal(2, engine.Cursor);
    }

    [Fact]
    public void MultilineMotions_WorkOnEngineMirror()
    {
        // 引擎镜像上的多行 motion（j/k/^/$ 联动）— $ 对齐基准 endOfLogicalLine（\n 位或文末）
        var engine = Normal("aaa\nbb\nccccc", 1);
        engine.HandleKey(Key("j")); // 列 1 → 行 1 偏移 5
        Assert.Equal(5, engine.Cursor);
        engine.HandleKey(Key("j")); // 列 1 → 行 2 偏移 8
        Assert.Equal(8, engine.Cursor);
        engine.HandleKey(Key("$")); // 末行行尾 = 文末 12
        Assert.Equal(12, engine.Cursor);
        engine.HandleKey(Key("^")); // 首非空白
        Assert.Equal(7, engine.Cursor);
    }
}
