namespace NativeTui.Tests.Repl;

/// <summary>
/// PromptTransparencyEngine 行为测试（TASK079 用户指令 — 另开一屏查看首次注入内容）：
/// /prompt → 折叠项展开全文 + transcript 备用屏打开（滚动/鼠标/搜索全基建，与子代理查看同模式）
/// + 滚动定位顶部；transcript 激活时再 /prompt → 关闭。普通消息原样透传。
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

    [Fact]
    public void Submit_Prompt_ExpandsItem_OpensTranscript_ScrollsTop()
    {
        var (engine, inner, shell) = Create("【首次注入的系统提示词】测试快照正文");

        engine.Submit("/prompt");

        Assert.Empty(inner.Submits);
        var item = shell.View.Messages.Items.OfType<PromptContextItem>().Single();
        Assert.True(item.Expanded);
        Assert.Contains("测试快照正文", item.ExpandedContent);
        Assert.True(shell.Transcript.IsActive);
        Assert.Equal(0, shell.Transcript.ScrollOffset);
    }

    [Fact]
    public void Submit_Prompt_WhenTranscriptActive_Closes()
    {
        var (engine, inner, shell) = Create("快照");
        engine.Submit("/prompt");
        Assert.True(shell.Transcript.IsActive);

        engine.Submit("/prompt");

        Assert.False(shell.Transcript.IsActive);
        Assert.True(shell.View.Messages.Items.OfType<PromptContextItem>().Single().Expanded, "展开态保持 — ctrl+o 重进仍可见全文");
    }

    [Fact]
    public void Submit_PlainText_PassesThroughToInner()
    {
        var (engine, inner, shell) = Create("快照");

        engine.Submit("普通消息");

        Assert.Equal(["普通消息"], inner.Submits);
        Assert.False(shell.Transcript.IsActive);
    }

    [Fact]
    public void Submit_OtherSlashCommand_PassesThrough()
    {
        var (engine, inner, shell) = Create("快照");

        engine.Submit("/model");

        Assert.Equal(["/model"], inner.Submits);
    }
}
