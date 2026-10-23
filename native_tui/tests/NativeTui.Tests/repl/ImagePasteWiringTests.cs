namespace NativeTui.Tests.Repl;

/// <summary>
/// 图片粘贴路径接线测试（TASK066 板块 E 连线 E4 — 粘贴通道识别 + 附件管理 + 占位）：
/// ReplShell Paste 分支分桶分派（对齐基准 usePasteHandler.ts:126-175 决策序）—
/// 纯文本零破坏 / 图片路径登记附件 + [Image #N] 占位 / 委托 null 降级纯文本 /
/// 混合粘贴分桶 / 孤儿修剪（占位被删附件清除 — PromptInput.tsx:1185-1200）/
/// 提交冻结（快照 + 登记簿清空归下回合 + 占位不展开上屏 — handlePromptSubmit.ts:174-185）/
/// 排队冻结（快照随队列发送时上屏 — handlePromptSubmit.ts:334-343 enqueue 携带）。
/// </summary>
public sealed class ImagePasteWiringTests
{
    /// <summary>固定尺寸源（80×30 — 行数阈值判定确定性：maxVisibleLines = min(30-10, 2) = 2，
    /// 摆脱测试进程 Console 环境依赖，对齐 TerminalSizeSource 测试注入先例）。</summary>
    private sealed class FixedSizeSource(int cols, int rows) : ITerminalSizeSource
    {
        public (int Cols, int Rows) Current => (cols, rows);
    }

    /// <summary>构造外壳（SeedContent 对齐 Run 首步；委托可空 = 无文件读取能力降级形态）。</summary>
    private static (ReplShell Shell, FakeReplEngine Engine) CreateShell(Func<string, ImageAttachment?>? loadImageFromFile = null) {
        var engine = new FakeReplEngine();
        var shell = new ReplShell(engine, new BannerItem("0.0.0", "m", "b", "C:\\"), new StringWriter(),
            sizeSource: new FixedSizeSource(80, 30), loadImageFromFile: loadImageFromFile);
        shell.SeedContent();
        return (shell, engine);
    }

    /// <summary>粘贴块注入（HandleRawKeyPress 直入 — 对齐 bracketed paste 到达形态）。</summary>
    private static void Paste(ReplShell shell, string content) =>
        shell.HandleRawKeyPress(new KeyPress(KeyKind.Paste, content));

    /// <summary>busy 置位 / 回合结束（TurnStarted → TurnCompleted 事件对）。</summary>
    private static void MakeBusy(ReplShell shell, FakeReplEngine engine) {
        engine.Events.Enqueue(new BridgeEvent.TurnStarted());
        shell.DrainEngine();
    }

    private static void EndTurn(ReplShell shell, FakeReplEngine engine) {
        engine.Events.Enqueue(new BridgeEvent.TurnCompleted());
        shell.DrainEngine();
    }

    /// <summary>图片文件读取委托测试替身 — 路径含 "a.png" 返回附件，其余 null（读失败形态）。</summary>
    private static ImageAttachment? StubLoader(string path) =>
        path.Contains("a.png", StringComparison.Ordinal)
            ? ImageAttachment.Of("QUJD", mediaType: "image/png", filename: "a.png", sourcePath: path)
            : null;

    // ==== 纯文本粘贴零破坏（无图片路径 — InputBuffer.Paste 原行为）====

    [Fact]
    public void PlainTextPaste_NoImagePath_KeepsOriginalBehavior()
    {
        var (shell, _) = CreateShell(StubLoader);

        Paste(shell, "hello world");

        Assert.Equal("hello world", shell.Input.Text);
        Assert.Equal(0, shell.Attachments.Count);
    }

    [Fact]
    public void PlainTextPaste_Multiline_InsertedAsText()
    {
        var (shell, _) = CreateShell(StubLoader);

        Paste(shell, "line1\nline2\nline3");

        Assert.Equal("line1\nline2\nline3", shell.Input.Text);
        Assert.Equal(0, shell.Attachments.Count);
    }

    // ==== 图片路径粘贴（委托返回附件 → 占位插入 + 附件登记 + 无文本进 Paste）====

    [Fact]
    public void ImagePathPaste_RegistersAttachmentAndInsertsPlaceholder()
    {
        var (shell, _) = CreateShell(StubLoader);

        Paste(shell, @"C:\pics\a.png");

        Assert.Equal("[Image #1]", shell.Input.Text);
        Assert.Equal(1, shell.Attachments.Count);
        var registered = shell.Attachments.TryGet(1);
        Assert.NotNull(registered);
        Assert.Equal("a.png", registered!.Filename);
        Assert.Equal(@"C:\pics\a.png", registered.SourcePath);
    }

    [Fact]
    public void MultipleImagePaths_RegisterSequentiallyWithSpaceSeparatedPlaceholders()
    {
        var (shell, _) = CreateShell(StubLoader);

        // 同一粘贴块两行图片路径（部分终端多图拖拽换行分隔形态）
        Paste(shell, "C:\\pics\\a.png\nC:\\pics\\a.png");

        // 占位间空格分隔（基准 pendingSpaceAfterPill 同事件内 prefix 语义）
        Assert.Equal("[Image #1] [Image #2]", shell.Input.Text);
        Assert.Equal(2, shell.Attachments.Count);
    }

    // ==== 委托 null 降级（路径按纯文本粘贴）====

    [Fact]
    public void ImagePathPaste_NoLoaderCapability_DegradesToPlainText()
    {
        var (shell, _) = CreateShell(loadImageFromFile: null);

        Paste(shell, @"C:\pics\a.png");

        // 无文件读取能力 — 图片路径整体降级纯文本粘贴（零行为变化）
        Assert.Equal(@"C:\pics\a.png", shell.Input.Text);
        Assert.Equal(0, shell.Attachments.Count);
    }

    [Fact]
    public void ImagePathPaste_LoaderFails_DegradesFailedLineToText()
    {
        var (shell, _) = CreateShell(StubLoader);

        // b.png 读失败（委托 null）— 该行回退非图片处理
        Paste(shell, @"C:\pics\b.png");

        Assert.Equal(@"C:\pics\b.png", shell.Input.Text);
        Assert.Equal(0, shell.Attachments.Count);
    }

    // ==== 混合粘贴（图片行 + 文本行分桶）====

    [Fact]
    public void MixedPaste_SplitsImageLinesAndTextLines()
    {
        var (shell, _) = CreateShell(StubLoader);

        Paste(shell, "C:\\pics\\a.png\nsome plain question");

        // 占位先插（图片行处理序），非图片行拼接走 Paste 纯文本链
        Assert.Equal("[Image #1]some plain question", shell.Input.Text);
        Assert.Equal(1, shell.Attachments.Count);
    }

    [Fact]
    public void MixedPaste_LoaderFailsOnImageLine_FailedLineFallsBackToText()
    {
        var (shell, _) = CreateShell(StubLoader);

        // 图片行读失败回退文本桶 + 非图片行 — 全部行有文本进 Paste，无占位
        Paste(shell, "C:\\pics\\b.png\nplain text");

        Assert.True(shell.Input.Text.Contains(@"C:\pics\b.png", StringComparison.Ordinal));
        Assert.Contains("plain text", shell.Input.Text);
        Assert.Equal(0, shell.Attachments.Count);
    }

    // ==== 孤儿修剪（删掉占位后附件清除 — 通过 AttachmentList 断言面）====

    [Fact]
    public void BackspaceDeletesPlaceholder_PrunesOrphanedAttachment()
    {
        var (shell, _) = CreateShell(StubLoader);
        Paste(shell, @"C:\pics\a.png");
        Assert.Equal(1, shell.Attachments.Count);

        // 退格 chip 整删 [Image #1]（InputBuffer 既有能力）→ HandleRawKeyPress 文本变化处修剪孤儿
        shell.HandleRawKeyPress(new KeyPress(KeyKind.Backspace, ""));

        Assert.Equal(string.Empty, shell.Input.Text);
        Assert.Equal(0, shell.Attachments.Count);
    }

    [Fact]
    public void EditingTextWithoutTouchingPlaceholder_KeepsAttachment()
    {
        var (shell, _) = CreateShell(StubLoader);
        Paste(shell, @"C:\pics\a.png");

        // 占位后输入文字（文本变化但占位仍在）→ 附件不误删
        shell.HandleRawKeyPress(new KeyPress(KeyKind.Char, "?"));

        Assert.Equal("[Image #1]?", shell.Input.Text);
        Assert.Equal(1, shell.Attachments.Count);
    }

    // ==== 提交冻结（提交后附件清空归位 + 占位不展开 + 附件项上屏）====

    [Fact]
    public void Submit_FreezesAttachmentsAndClearsRegistry()
    {
        var (shell, engine) = CreateShell(StubLoader);
        Paste(shell, @"C:\pics\a.png");

        shell.Submit();

        // G3 — 占位 [Image #1] 从引擎提交文本剥离（基准 Image refs become content blocks：占位是
        // TUI 显示形态引擎不收，图片数据走附件通道）；纯占位提交剥离后为空文本 + 1 附件走图片重载
        Assert.Equal([("", 1)], engine.SubmitsWithImages);
        Assert.Equal([""], engine.Submits);
        // 登记簿清空归下回合
        Assert.Equal(0, shell.Attachments.Count);
        // 附件占位项上屏（用户消息后挂）
        Assert.Single(shell.View.Messages.Items.OfType<ImageAttachmentItem>());
    }

    [Fact]
    public void Submit_MixedText_KeepsPlaceholderInlineAndShowsAttachmentItem()
    {
        var (shell, engine) = CreateShell(StubLoader);
        Paste(shell, "C:\\pics\\a.png\nlook at this");

        shell.Submit();

        // G3 — 引擎收剥离后纯文本 + 附件；上屏显示形态保留占位（显示/提交双形态分离）
        Assert.Equal([("look at this", 1)], engine.SubmitsWithImages);
        Assert.Equal(["look at this"], engine.Submits);
        Assert.Equal(0, shell.Attachments.Count);
        var item = Assert.Single(shell.View.Messages.Items.OfType<ImageAttachmentItem>());
        Assert.Equal(1, item.ImageId);
        Assert.Equal("a.png", item.Filename);
        Assert.Contains(shell.View.Messages.Items,
            m => m is UserMessageItem u && u.Text == "[Image #1]look at this");
    }

    [Fact]
    public void Submit_EmptyInput_IgnoresSubmit()
    {
        var (shell, engine) = CreateShell(StubLoader);

        // 空文本不提交（基准 input.trim() === '' return — 纯附件无占位文本不构成提交）
        shell.Submit();

        Assert.Empty(engine.Submits);
    }

    [Fact]
    public void NewAttachmentAfterSubmit_RegistersFromIdOneAgain()
    {
        var (shell, _) = CreateShell(StubLoader);
        Paste(shell, @"C:\pics\a.png");
        shell.Submit();

        // 提交后新附件归下回合 — 编号从头分配（登记簿 Clear 重置，对齐基准 setPastedContents({})）
        Paste(shell, @"C:\pics\a.png");

        Assert.Equal("[Image #1]", shell.Input.Text);
        Assert.Equal(1, shell.Attachments.Count);
    }

    // ==== 排队冻结（提交时附件冻结随队列 — 发送时上屏）====

    [Fact]
    public void BusySubmit_QueueCarriesFrozenAttachments_ShownWhenSent()
    {
        var (shell, engine) = CreateShell(StubLoader);
        MakeBusy(shell, engine);
        Paste(shell, @"C:\pics\a.png");
        Assert.Equal(1, shell.Attachments.Count);

        shell.Submit();
        // 排队期间登记簿已清（快照随队列），不上滚动区
        Assert.Equal(0, shell.Attachments.Count);
        Assert.DoesNotContain(shell.View.Messages.Items, m => m is ImageAttachmentItem);
        Assert.Empty(engine.Submits);

        EndTurn(shell, engine);

        // 发送时附件项随用户消息上屏（基准 enqueue 携带 pastedContents 执行时带图语义）；
        // G3 — 引擎收剥离后文本 + 附件（排队泵走 SubmitToEngine 统一分派）
        Assert.Equal([("", 1)], engine.SubmitsWithImages);
        Assert.Equal([""], engine.Submits);
        Assert.Single(shell.View.Messages.Items.OfType<ImageAttachmentItem>());
    }

    // ==== G3 附件提交通道（无附件零破坏 + bridge DTO 投影字段）====

    [Fact]
    public void Submit_WithoutAttachments_UsesSingleParameterSubmit_NoBreakage()
    {
        var (shell, engine) = CreateShell(StubLoader);
        Type(shell, "纯文本提交");

        shell.Submit();

        // 零破坏 — 无附件走原单参 Submit（不进图片重载记录面）
        Assert.Empty(engine.SubmitsWithImages);
        Assert.Equal(["纯文本提交"], engine.Submits);
    }

    [Fact]
    public void Submit_WithAttachments_ProjectsBridgeDtoFields()
    {
        var (shell, engine) = CreateShell(StubLoader);
        Paste(shell, "C:\\pics\\a.png");

        shell.Submit();

        // bridge DTO 投影字段透传（Base64/MediaType/Filename 三字段 — 与 Core 域解耦的协议形态）
        var projected = Assert.Single(engine.SubmittedImages);
        var image = Assert.Single(projected);
        Assert.Equal("QUJD", image.Base64);
        Assert.Equal("image/png", image.MediaType);
        Assert.Equal("a.png", image.Filename);
    }

    /// <summary>逐字符输入文本（无粘贴 — 纯文本提交零破坏对照用）。</summary>
    private static void Type(ReplShell shell, string text)
    {
        foreach (var ch in text)
        {
            shell.HandleRawKeyPress(new KeyPress(KeyKind.Char, ch.ToString()));
        }
    }

    // ==== 多行可见行数上限（纯文本链零破坏 — 折叠语义不因分桶接线变化）====

    [Fact]
    public void LongMixedPaste_TextLinesStillFolded()
    {
        var (shell, _) = CreateShell(StubLoader);

        // 非图片行 4 行（3 个换行分隔符 > maxVisibleLines=2）→ [Pasted text #N +M lines]
        // 折叠登记（既有 Paste 链零破坏 — 分桶接线不改变文本桶折叠语义）
        Paste(shell, "C:\\pics\\a.png\nw\nx\ny\nz");

        Assert.StartsWith("[Image #1][Pasted text #", shell.Input.Text);
        Assert.Equal(1, shell.Attachments.Count);
    }
}
