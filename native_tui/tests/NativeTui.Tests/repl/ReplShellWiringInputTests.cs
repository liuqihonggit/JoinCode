namespace NativeTui.Tests.Repl;

/// <summary>
/// S11 统一接线 — 输入编辑域冒烟（S7 接线点 1/2/3/4/6）：
/// shift+enter 换行不提交、ctrl+_/ctrl+shift+- undo 键位、多行上下键行间垂直移动
///（首/末行回退历史导航）、提交全文（粘贴引用 [Pasted text #N] 展开为原文发引擎）、
/// bracketed paste 事件整块进入 InputBuffer.Paste 清洗折叠链。
/// </summary>
public sealed class ReplShellWiringInputTests
{
    /// <summary>构造测试 shell（固定尺寸源脱离 Console 环境）。</summary>
    private static (ReplShell Shell, FakeReplEngine Engine, FakeTerminalSizeSource Size) CreateShell()
    {
        var engine = new FakeReplEngine();
        var banner = new BannerItem("1.0.0", "test-model", "API 按量计费", "/tmp");
        var size = new FakeTerminalSizeSource(80, 24);
        var shell = new ReplShell(engine, banner, new StringWriter(), sizeSource: size);
        shell.SeedContent();
        return (shell, engine, size);
    }

    /// <summary>模拟键入（经 HandleKey 全链 — 触发建议刷新链）。</summary>
    private static void Type(ReplShell shell, string text)
    {
        foreach (var c in text)
        {
            shell.HandleKey(new ConsoleKeyInfo(c, ConsoleKey.None, false, false, false));
        }
    }

    /// <summary>接线点1 — shift+enter 插入换行符不提交（多行编辑开启；引擎零透传）。</summary>
    [Fact]
    public void ShiftEnter_InsertsNewline_WithoutSubmitting()
    {
        var (shell, engine, _) = CreateShell();
        Type(shell, "第一行");

        shell.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: true, alt: false, control: false));

        Assert.Equal("第一行\n", shell.Input.Text);
        Assert.Empty(engine.Submits);
        Assert.True(shell.Input.IsMultiline);
    }

    /// <summary>接线点1 — 裸 enter 仍提交（既有语义零回归）。</summary>
    [Fact]
    public void PlainEnter_SubmitsAsBefore()
    {
        var (shell, engine, _) = CreateShell();
        Type(shell, "hello");

        shell.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: false, alt: false, control: false));

        Assert.Equal(["hello"], engine.Submits);
        Assert.Equal(string.Empty, shell.Input.Text);
    }

    /// <summary>接线点2 — ctrl+_/ctrl+shift+-（\x1f 归一 Ctrl+"_"）undo：撤销上次编辑。</summary>
    [Fact]
    public void CtrlUnderscore_UndoesLastEdit()
    {
        var (shell, _, _) = CreateShell();
        Type(shell, "abc");

        shell.HandleRawKeyPress(new KeyPress(KeyKind.Char, "_", Ctrl: true));

        Assert.Equal("ab", shell.Input.Text);
    }

    /// <summary>接线点2 — undo 栈空时 ctrl+_ 无操作（不崩溃不误删）。</summary>
    [Fact]
    public void CtrlUnderscore_EmptyStack_NoOp()
    {
        var (shell, _, _) = CreateShell();

        shell.HandleRawKeyPress(new KeyPress(KeyKind.Char, "_", Ctrl: true));

        Assert.Equal(string.Empty, shell.Input.Text);
    }

    /// <summary>接线点4 — 多行内部上下键行间垂直移动（不触发历史导航；光标保持目标列）。</summary>
    [Fact]
    public void MultilineArrows_MoveVertically_BeforeHistoryNavigation()
    {
        var (shell, _, _) = CreateShell();
        Type(shell, "long line one");
        shell.HandleKey(new ConsoleKeyInfo('\r', ConsoleKey.Enter, shift: true, alt: false, control: false));
        Type(shell, "ab");
        shell.SeedHistory(["历史条目"]);

        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));

        Assert.Equal(0, shell.Input.CursorLineIndex);
        Assert.Equal("long line one\nab", shell.Input.Text);
        // 首行再按上 = 回退历史导航（行间移动不可达时）
        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));
        Assert.Equal("历史条目", shell.Input.Text);
    }

    /// <summary>接线点4 — 单行输入上下键仍走历史导航（多行分支不劫持）。</summary>
    [Fact]
    public void SingleLineArrows_StillNavigateHistory()
    {
        var (shell, _, _) = CreateShell();
        shell.SeedHistory(["hist"]);

        shell.HandleKey(new ConsoleKeyInfo('\0', ConsoleKey.UpArrow, false, false, false));

        Assert.Equal("hist", shell.Input.Text);
    }

    /// <summary>接线点3 — 提交全文：粘贴折叠引用 [Pasted text #1] 提交时展开回原文发引擎
    ///（上屏/历史保留折叠显示形态 — 对齐基准：登记簿随提交清空，历史条目为折叠字面量不可再展开）。</summary>
    [Fact]
    public void Submit_PasteReference_SendsExpandedTextToEngine()
    {
        var (shell, engine, size) = CreateShell();
        // 大块粘贴（超默认折叠阈值）→ 登记引用形态
        var pasted = string.Join("\n", Enumerable.Repeat("line", 30));
        shell.HandleRawKeyPress(new KeyPress(KeyKind.Paste, pasted));

        Assert.Contains("[Pasted text #1", shell.Input.Text);
        shell.Submit();

        Assert.Equal([pasted], engine.Submits);
        var user = shell.View.Messages.Items.OfType<UserMessageItem>().Single();
        Assert.Contains("[Pasted text #1", user.Text);
        Assert.Contains("[Pasted text #1", shell.History.Entries[^1]);
    }

    /// <summary>折叠可见行数上限 = min(rows-10, 2)（基准 PromptInput.tsx:1220 Math.min(rows - 10, 2)）—
    /// 24 行终端粘贴 5 行（超上限 2、远小于 rows-10=14）必须折叠，防原文直插挤爆输入区。</summary>
    [Fact]
    public void Paste_Multiline_FoldsAtMinRowsCap()
    {
        var (shell, _, _) = CreateShell();
        shell.HandleRawKeyPress(new KeyPress(KeyKind.Paste, "a\nb\nc\nd\ne"));
        Assert.Contains("[Pasted text #1", shell.Input.Text);
    }

    /// <summary>接线点3 — 忙时排队同样保存展开形态：回合结束出队发送原文（登记簿已清不可再展开）。</summary>
    [Fact]
    public void Submit_WhileBusy_QueuesExpandedForm()
    {
        var (shell, engine, _) = CreateShell();
        var pasted = string.Join("\n", Enumerable.Repeat("q", 30));
        shell.HandleRawKeyPress(new KeyPress(KeyKind.Paste, pasted));
        engine.Events.Enqueue(new BridgeEvent.TurnStarted());
        shell.DrainEngine();

        shell.Submit();
        engine.Events.Enqueue(new BridgeEvent.TurnCompleted());
        shell.DrainEngine();

        Assert.Equal([pasted], engine.Submits);
        var user = shell.View.Messages.Items.OfType<UserMessageItem>().Single();
        Assert.Contains("[Pasted text #1", user.Text);
    }

    /// <summary>接线点6 — bracketed paste 事件整块进入（小文本直插原文；\r 清洗归一 \n）。</summary>
    [Fact]
    public void PasteEvent_SmallText_InsertsCleaned()
    {
        var (shell, _, _) = CreateShell();

        shell.HandleRawKeyPress(new KeyPress(KeyKind.Paste, "a\r\nb"));

        Assert.Equal("a\nb", shell.Input.Text);
    }

    /// <summary>接线点6 — WriteExit 输出含对称恢复序列（光标显示 + bracketed paste Disable）。</summary>
    [Fact]
    public void WriteExit_EmitsCursorShow_AndPasteDisable()
    {
        var writer = new StringWriter();
        var engine = new FakeReplEngine();
        var shell = new ReplShell(engine, new BannerItem("1", "m", "b", "/tmp"), writer, sizeSource: new FakeTerminalSizeSource(80, 24));
        shell.SeedContent();

        shell.WriteExit();

        var output = writer.ToString();
        Assert.Contains(CursorLifecycle.Show, output);
        Assert.Contains(DecPrivateMode.Disable(DecPrivateModeKind.BracketedPaste), output);
    }
}
