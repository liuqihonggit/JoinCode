namespace NativeTui.Tests.Repl;

/// <summary>
/// PromptTransparencyEngine 行为测试（TASK079 用户指令 — 用户应清晰知道注入了什么）：
/// /prompt 拦截把宿主注入的快照（首次注入的系统提示词全文 + 工具清单）dump 进 transcript，
/// 其余输入原样透传给内层引擎。
/// </summary>
public sealed class PromptTransparencyEngineTests
{
    private static (PromptTransparencyEngine Engine, FakeReplEngine Inner, ReplShell Shell) Create(string snapshot)
    {
        var inner = new FakeReplEngine();
        var engine = new PromptTransparencyEngine(inner);
        var banner = new BannerItem("1.0.0", "test-model", "API 按量计费", "/tmp");
        var shell = new ReplShell(engine, banner, new StringWriter(), promptContextSnapshot: snapshot,
            promptContextInventory: () => ("【工具清单】" + Environment.NewLine + "  bash (system): 运行命令", 505, 35));
        shell.SeedContent();
        engine.Attach(shell);
        return (engine, inner, shell);
    }

    private static PromptDetailDialog? PageDialog(ReplShell shell) =>
        shell.View.Messages.Items.OfType<PromptDetailDialog>().FirstOrDefault();

    [Fact]
    public void Submit_Prompt_OpensPageModal_AndSecondPromptCloses()
    {
        var (engine, inner, shell) = Create("【首次注入的系统提示词】测试快照正文");

        engine.Submit("/prompt");

        Assert.Empty(inner.Submits);
        Assert.True(shell.IsPromptPageOpen);
        var page = PageDialog(shell);
        Assert.NotNull(page);
        Assert.Contains("测试快照正文", page!.SnapshotForTest);
        Assert.True(page.MeasureHeight(80) > 7, "页面高度 = 头部 7 行 + 快照正文");

        engine.Submit("/prompt");
        Assert.False(shell.IsPromptPageOpen);
        Assert.Null(PageDialog(shell));
    }

    [Fact]
    public void Submit_PlainText_PassesThroughToInner()
    {
        var (engine, inner, shell) = Create("快照");

        engine.Submit("普通消息");

        Assert.Equal(["普通消息"], inner.Submits);
        Assert.False(shell.IsPromptPageOpen);
    }

    [Fact]
    public void Submit_OtherSlashCommand_PassesThrough()
    {
        var (engine, inner, shell) = Create("快照");

        engine.Submit("/model");

        Assert.Equal(["/model"], inner.Submits);
    }
}
