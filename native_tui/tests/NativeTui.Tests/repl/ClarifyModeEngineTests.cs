namespace NativeTui.Tests.Repl;

/// <summary>
/// ClarifyModeEngine 行为测试（TASK078 — TUI 原生需求澄清）：
/// /?? //ask 拦截为澄清回合（命令名剥离 + 系统提示词组合 + 空参数引导），其余输入原样透传，
/// 非拦截成员委托内层。Attach 传播（装饰器 → 内层 CommandPickerEngine）由 /?? E2E + 拾取器既有
/// Attach 测试族覆盖，此处不重复装配整壳。
/// </summary>
public sealed class ClarifyModeEngineTests {
    private const string SystemPrompt = "【需求澄清模式】测试提示词";

    [Fact]
    public void Submit_ArgsAfterCommand_ComposesClarifyTurn() {
        var fake = new FakeReplEngine();
        var engine = new ClarifyModeEngine(fake, SystemPrompt);
        engine.Submit("/?? 做个爬虫");
        var submit = Assert.Single(fake.Submits);
        Assert.StartsWith(SystemPrompt, submit);
        Assert.Contains("用户需求: 做个爬虫", submit);
        Assert.DoesNotContain("/??", submit);
    }

    [Fact]
    public void Submit_AskAlias_InterceptsToo() {
        var fake = new FakeReplEngine();
        var engine = new ClarifyModeEngine(fake, SystemPrompt);
        engine.Submit("/ask 写个 todo 应用");
        var submit = Assert.Single(fake.Submits);
        Assert.StartsWith(SystemPrompt, submit);
        Assert.Contains("用户需求: 写个 todo 应用", submit);
    }

    [Fact]
    public void Submit_BareCommand_UsesEmptyArgsInstruction() {
        var fake = new FakeReplEngine();
        var engine = new ClarifyModeEngine(fake, SystemPrompt);
        engine.Submit("/??");
        var submit = Assert.Single(fake.Submits);
        Assert.StartsWith(SystemPrompt, submit);
        Assert.Contains("用户尚未描述需求", submit);
    }

    [Fact]
    public void Submit_PlainText_PassesThroughUntouched() {
        var fake = new FakeReplEngine();
        var engine = new ClarifyModeEngine(fake, SystemPrompt);
        engine.Submit("普通消息");
        Assert.Equal(["普通消息"], fake.Submits);
    }

    [Fact]
    public void Submit_OtherSlashCommand_PassesThroughUntouched() {
        var fake = new FakeReplEngine();
        var engine = new ClarifyModeEngine(fake, SystemPrompt);
        engine.Submit("/model");
        Assert.Equal(["/model"], fake.Submits);
    }

    [Fact]
    public void Submit_WithImages_ClarifyCarriesAttachments() {
        var fake = new FakeReplEngine();
        var engine = new ClarifyModeEngine(fake, SystemPrompt);
        IReadOnlyList<BridgeImageAttachment> images = [new BridgeImageAttachment("aGk=", "image/png", "shot.png")];
        engine.Submit("/?? 看图", images);
        var submit = Assert.Single(fake.SubmitsWithImages);
        Assert.StartsWith(SystemPrompt, submit.Text);
        Assert.Equal(1, submit.Images);
    }

    [Fact]
    public void NonSubmitMembers_DelegateToInner() {
        var fake = new FakeReplEngine();
        var engine = new ClarifyModeEngine(fake, SystemPrompt);
        engine.Interrupt();
        engine.SetPermissionMode("acceptEdits");
        engine.StartNewConversation();
        engine.StopBackgroundTask("fork", "t1");
        Assert.Equal(1, fake.Interrupts);
        Assert.Equal(["acceptEdits"], fake.PermissionModes);
        Assert.Equal(1, fake.NewConversations);
        Assert.True(engine.IsAlive);
    }
}
