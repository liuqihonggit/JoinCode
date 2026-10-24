// JCC1018 豁免: 本测试以 Windows 路径字面量为被测数据（Windows 形态行为/守护判定/端点解析等），
// 被测对象即 Windows 路径语义，非跨平台误用 — 新增跨平台匹配测试请用 Path.Combine(GetTempPath()) 构造。
#pragma warning disable JCC1018
namespace NativeTui.Tests.Repl;

/// <summary>
/// 剪贴板贴图接线测试（TASK066 板块 E 收尾 — chat:imagePaste 键消费方）：
/// 键位表显式注入（KeybindingProfile.Of 绑 alt+v / ctrl+v — 跨平台测试稳定性：
/// 默认表 win=alt+v / 其余=ctrl+v 平台分支，接线语义两入口等价）；
/// alt+v（Meta 路径 HandleMetaKey 表查询 → DispatchCtrlAction）与 ctrl+v
///（Ctrl 路径 HandleCtrlKey MatchAction → DispatchCtrlAction）双入口 →
/// 委托触发 → 附件登记 + [Image #N] 占位（对齐基准 PromptInput.tsx:1619-1635
/// handleImagePaste → onImagePaste :1151-1183 同终点）；null 委托能力降级 no-op；
/// 剪贴板无图/读取失败 → footer 通知降级（基准 :1624-1632 immediate + 1000ms）；
/// 防重入（读取会话进行中连按不重复触发 — LaunchExternalEditor 先例）；
/// Meta 字面编辑键回归守卫（HandleMetaKey 表驱动升级零破坏 — alt+b/d 编辑语义保持）。
/// </summary>
public sealed class ClipboardImagePasteTests
{
    /// <summary>固定尺寸源（80×30 — 摆脱测试进程 Console 环境依赖，
    /// 对齐 TerminalSizeSource 测试注入先例）。</summary>
    private sealed class FixedSizeSource(int cols, int rows) : ITerminalSizeSource
    {
        public (int Cols, int Rows) Current => (cols, rows);
    }

    /// <summary>构造外壳（SeedContent 对齐 Run 首步；委托/键位表可空 = 降级/默认形态）。</summary>
    private static (ReplShell Shell, FakeReplEngine Engine) CreateShell(
        Func<Task<ImageAttachment?>>? readClipboardImage = null,
        KeybindingProfile? keybindings = null)
    {
        var engine = new FakeReplEngine();
        var shell = new ReplShell(engine, new BannerItem("0.0.0", "m", "b", "C:\\"),
            new StringWriter(), sizeSource: new FixedSizeSource(80, 30),
            keybindings: keybindings, readClipboardImage: readClipboardImage);
        shell.SeedContent();
        return (shell, engine);
    }

    /// <summary>显式 alt+v 键位表（Chat 桶 — Meta 路径测试；默认表平台分支跨 CI 不稳，
    /// 接线语义与平台选择正交）。</summary>
    private static KeybindingProfile AltVProfile() => KeybindingProfile.Of(
        [(KeybindingContext.Chat, KeybindingAction.ChatImagePaste, KeyChord.Parse("alt+v"))]);

    /// <summary>显式 ctrl+v 键位表（Chat 桶 — Ctrl 路径测试，非 Windows 平台默认形态等价）。</summary>
    private static KeybindingProfile CtrlVProfile() => KeybindingProfile.Of(
        [(KeybindingContext.Chat, KeybindingAction.ChatImagePaste, KeyChord.Parse("ctrl+v"))]);

    // ==== alt+v 触发（Meta 路径 — HandleMetaKey 表查询 → DispatchCtrlAction）====

    /// <summary>alt+v 触发剪贴板读取 → 附件登记 + 占位插入 — fire-and-forget 完成后
    /// DrainEngine 消费（基准 handleImagePaste :1621-1623 → onImagePaste :1151-1183：
    /// 附件登记 pastedContents + insertTextAtCursor(formatImageRef) 同终点）。</summary>
    [Fact]
    public async Task AltV_InvokesDelegate_RegistersAttachmentAndInsertsPlaceholder()
    {
        var (shell, _) = CreateShell(
            () => Task.FromResult<ImageAttachment?>(ImageAttachment.Of("QUJD", mediaType: "image/png")),
            AltVProfile());

        shell.HandleRawKeyPress(new KeyPress(KeyKind.Char, "v", Meta: true));
        await shell.ClipboardImageTask!;
        shell.DrainEngine();

        Assert.Equal("[Image #1]", shell.Input.Text);
        Assert.Equal(1, shell.Attachments.Count);
        var registered = shell.Attachments.TryGet(1);
        Assert.NotNull(registered);
        Assert.Equal("Pasted image", registered!.Filename);
        Assert.Equal("image/png", registered.MediaType);
        Assert.Equal("QUJD", registered.Base64);
    }

    /// <summary>剪贴板无图（委托返回 null）→ footer 通知降级 + 无附件不打扰输入框
    ///（基准 :1624-1632 addNotification "No image found in clipboard" priority
    /// immediate + timeoutMs 1000 — Immediate 抢占显示直接进当前通知槽）。</summary>
    [Fact]
    public async Task AltV_NoImageInClipboard_ShowsNotificationWithoutAttachment()
    {
        var (shell, _) = CreateShell(() => Task.FromResult<ImageAttachment?>(null), AltVProfile());

        shell.HandleRawKeyPress(new KeyPress(KeyKind.Char, "v", Meta: true));
        await shell.ClipboardImageTask!;
        shell.DrainEngine();

        Assert.Equal(0, shell.Attachments.Count);
        Assert.Equal(string.Empty, shell.Input.Text);
        Assert.Equal("剪贴板未找到图片", shell.View.Footer.Notifications!.Current);
    }

    /// <summary>委托未注入（null = 无剪贴板读取能力）→ 按键 no-op 零行为变化
    ///（对齐 loadImageFromFile null 降级先例 — 无任务无附件无通知）。</summary>
    [Fact]
    public void AltV_NullDelegate_NoOp()
    {
        var (shell, _) = CreateShell(readClipboardImage: null, keybindings: AltVProfile());

        shell.HandleRawKeyPress(new KeyPress(KeyKind.Char, "v", Meta: true));

        Assert.Null(shell.ClipboardImageTask);
        Assert.Equal(0, shell.Attachments.Count);
        Assert.Equal(string.Empty, shell.Input.Text);
        // 无新增通知（Current 至多为 SeedContent 种子 effort 提示 — 剪贴板降级无通知）
        Assert.NotEqual("剪贴板未找到图片", shell.View.Footer.Notifications!.Current);
    }

    /// <summary>委托异常（IO 通道失败）→ 容错入队 null 信号 → 通知降级
    ///（ReadClipboardImageAsync catch IOException 容错集 — bash 直通容错先例）。</summary>
    [Fact]
    public async Task AltV_DelegateThrows_SwallowedAsNoImageNotification()
    {
        var (shell, _) = CreateShell(
            () => Task.FromException<ImageAttachment?>(new IOException("boom")),
            AltVProfile());

        shell.HandleRawKeyPress(new KeyPress(KeyKind.Char, "v", Meta: true));
        await shell.ClipboardImageTask!;
        shell.DrainEngine();

        Assert.Equal(0, shell.Attachments.Count);
        Assert.Equal("剪贴板未找到图片", shell.View.Footer.Notifications!.Current);
    }

    /// <summary>读取会话进行中防重入 — 挂起委托（TaskCompletionSource）期间第二次
    /// alt+v 不重复触发（委托单次调用）；完成后防重入解除，再按可再次触发
    ///（对齐 _externalEditorActive 防重入先例 — PowerShell 读取 ~1s 期间连按不叠加）。</summary>
    [Fact]
    public async Task AltV_WhileReadInProgress_SecondPressIgnoredUntilDrained()
    {
        var pending = new TaskCompletionSource<ImageAttachment?>();
        var calls = 0;
        var (shell, _) = CreateShell(
            () => { calls++; return pending.Task; },
            AltVProfile());

        shell.HandleRawKeyPress(new KeyPress(KeyKind.Char, "v", Meta: true));
        shell.HandleRawKeyPress(new KeyPress(KeyKind.Char, "v", Meta: true));
        Assert.Equal(1, calls);

        pending.SetResult(ImageAttachment.Of("QUJD"));
        await shell.ClipboardImageTask!;
        shell.DrainEngine();
        Assert.Equal("[Image #1]", shell.Input.Text);

        // 防重入随结果消费解除 — 再次按键开启新读取会话
        shell.HandleRawKeyPress(new KeyPress(KeyKind.Char, "v", Meta: true));
        Assert.Equal(2, calls);
    }

    // ==== ctrl+v 触发（Ctrl 路径 — HandleCtrlKey MatchAction → DispatchCtrlAction）====

    /// <summary>ctrl+v（非 Windows 平台默认键位形态）→ 同一 DispatchCtrlAction 分派面 →
    /// 剪贴板读取 + 附件登记（基准 useKeybindings 单消费面 — Meta/Ctrl 两侧入口等价）。</summary>
    [Fact]
    public async Task CtrlV_CtrlPath_TriggersSameClipboardDispatch()
    {
        var (shell, _) = CreateShell(
            () => Task.FromResult<ImageAttachment?>(ImageAttachment.Of("QUJD")),
            CtrlVProfile());

        shell.HandleRawKeyPress(new KeyPress(KeyKind.Char, "v", Ctrl: true));
        await shell.ClipboardImageTask!;
        shell.DrainEngine();

        Assert.Equal("[Image #1]", shell.Input.Text);
        Assert.Equal(1, shell.Attachments.Count);
    }

    // ==== Meta 字面编辑键回归守卫（HandleMetaKey 表驱动升级零破坏）====

    /// <summary>表未绑定的 Meta 编辑键仍走字面 switch — 光标移 "ab |cd" 后 alt+d
    ///（KillWordAfter）删 "cd" 行为保持（HandleMetaKey 升级引入表查询前置：未命中/
    /// 未接管键回落原编辑路径，meta+b/f/d/y 编辑语义零变化）。</summary>
    [Fact]
    public void MetaEditingKey_NotInProfile_StillRoutesToLiteralEditing()
    {
        var (shell, _) = CreateShell(keybindings: AltVProfile());
        Type(shell, "ab cd");

        // 光标从尾部左移 2 位到 "ab |cd"（c 前）— KillWordAfter 删后续词
        shell.HandleRawKeyPress(new KeyPress(KeyKind.LeftArrow, ""));
        shell.HandleRawKeyPress(new KeyPress(KeyKind.LeftArrow, ""));
        shell.HandleRawKeyPress(new KeyPress(KeyKind.Char, "d", Meta: true));

        Assert.Equal("ab ", shell.Input.Text);
        Assert.Equal(0, shell.Attachments.Count);
    }

    /// <summary>逐字符输入文本（无粘贴 — 编辑键回归守卫用）。</summary>
    private static void Type(ReplShell shell, string text)
    {
        foreach (var ch in text)
        {
            shell.HandleRawKeyPress(new KeyPress(KeyKind.Char, ch.ToString()));
        }
    }
}
