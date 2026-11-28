namespace NativeTui.Tests.Repl;

/// <summary>
/// PromptTransparencyEngine 行为测试（TASK145 — 每轮注入提示词逐轮可见）：
/// PromptInjected 事件按到达顺序建折叠项；/prompt 打开最近一轮全文（transcript 备用屏）；
/// transcript 激活时再 /prompt → 关闭；普通消息原样透传。
/// </summary>
public sealed class PromptTransparencyEngineTests
{
    private static (PromptTransparencyEngine Engine, FakeReplEngine Inner, ReplShell Shell) Create()
    {
        var inner = new FakeReplEngine();
        var engine = new PromptTransparencyEngine(inner);
        var banner = new BannerItem("1.0.0", "test-model", "API 按量计费", "/tmp");
        var shell = new ReplShell(engine, banner, new StringWriter());
        shell.SeedContent();
        engine.Attach(shell);
        return (engine, inner, shell);
    }

    /// <summary>模拟引擎每轮预处理完成后的注入快照事件（会话内按顺序到达）。</summary>
    private static void InjectPrompt(FakeReplEngine inner, ReplShell shell, string promptBody)
    {
        inner.Events.Enqueue(new BridgeEvent.PromptInjected(promptBody));
        shell.DrainEngine();
    }

    [Fact]
    public void PromptInjected_CreatesPerRoundItems_InArrivalOrder()
    {
        var (_, inner, shell) = Create();

        InjectPrompt(inner, shell, "第一轮系统提示词");
        InjectPrompt(inner, shell, "第二轮系统提示词");

        var items = shell.View.Messages.Items.OfType<PromptContextItem>().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal(1, items[0].Round);
        Assert.Equal(2, items[1].Round);
        Assert.Equal("第一轮系统提示词", items[0].ExpandedContent);
        Assert.Equal("第二轮系统提示词", items[1].ExpandedContent);
        Assert.Contains("第 1 轮注入", items[0].Summary);
        Assert.Contains("第 2 轮注入", items[1].Summary);
    }

    [Fact]
    public void Submit_Prompt_OpensLatestRound_AndExpandsOnlyIt()
    {
        var (engine, inner, shell) = Create();
        InjectPrompt(inner, shell, "第一轮系统提示词");
        InjectPrompt(inner, shell, "第二轮系统提示词");

        engine.Submit("/prompt");

        Assert.DoesNotContain("/prompt", inner.Submits);
        var items = shell.View.Messages.Items.OfType<PromptContextItem>().ToList();
        Assert.False(items[0].Expanded);
        Assert.True(items[1].Expanded);
        Assert.True(shell.Transcript.IsActive);
        Assert.Equal(0, shell.Transcript.ScrollOffset);
    }

    [Fact]
    public void Submit_Prompt_WhenTranscriptActive_Closes()
    {
        var (engine, inner, shell) = Create();
        InjectPrompt(inner, shell, "快照");
        engine.Submit("/prompt");
        Assert.True(shell.Transcript.IsActive);

        engine.Submit("/prompt");

        Assert.False(shell.Transcript.IsActive);
        Assert.True(shell.View.Messages.Items.OfType<PromptContextItem>().Single().Expanded, "展开态保持 — 重进仍可见全文");
    }

    [Fact]
    public void TogglePromptContext_TargetedRound_ExpandsOnlyThatRound()
    {
        var (_, inner, shell) = Create();
        InjectPrompt(inner, shell, "第一轮系统提示词");
        InjectPrompt(inner, shell, "第二轮系统提示词");
        var first = shell.View.Messages.Items.OfType<PromptContextItem>().First();

        shell.TogglePromptContext(first);

        Assert.True(first.Expanded);
        Assert.False(shell.View.Messages.Items.OfType<PromptContextItem>().Last().Expanded);
        Assert.True(shell.Transcript.IsActive);
    }

    [Fact]
    public void Submit_Prompt_NoInjectionYet_ShowsNotice_DoesNotOpenTranscript()
    {
        var (engine, _, shell) = Create();

        engine.Submit("/prompt");

        Assert.False(shell.Transcript.IsActive);
    }

    [Fact]
    public void Submit_PlainText_PassesThroughToInner()
    {
        var (engine, inner, shell) = Create();

        engine.Submit("普通消息");

        Assert.Equal(["普通消息"], inner.Submits);
        Assert.False(shell.Transcript.IsActive);
    }

    [Fact]
    public void Submit_OtherSlashCommand_PassesThrough()
    {
        var (engine, inner, shell) = Create();

        engine.Submit("/model");

        Assert.Equal(["/model"], inner.Submits);
    }
}