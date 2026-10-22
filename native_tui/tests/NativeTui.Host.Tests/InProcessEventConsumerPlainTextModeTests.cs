namespace NativeTui.Host.Tests;

/// <summary>
/// TASK065 F6 命令输出纯文本模式测试 — 斜杠命令回合输出（/help 类缩进对齐文本）经
/// InProcessEventConsumer 上屏时跳过 Markdig 投影（AssistantMessage.Markdown = null →
/// 渲染走 AssistantMessageItem 纯文本，防 4 空格缩进被解释为代码块/框线表格错位）：
/// BeginCommandTurn 置位 → OnDone 发纯文本消息 + 自动复位；Reset 兜底复位；
/// 后续对话回合 Markdown 投影不受影响（markdown 非 null 快路径照旧）。
/// 端到端：DispatchSlashCommandAsync（/help 真实命令，fake DI）→ 事件流断言。
/// </summary>
[Collection(SlashCommandSerialCollection.CollectionName)]
public sealed class InProcessEventConsumerPlainTextModeTests
{
    /// <summary>命令回合置位 — OnDone 发 AssistantMessage 且 Markdown 为 null（跳过投影）。</summary>
    [Fact]
    public void BeginCommandTurn_OnDone_SuppressesMarkdownProjection()
    {
        var consumer = new InProcessEventConsumer();
        consumer.BeginCommandTurn();

        consumer.OnText("# 标题\n    缩进代码块样式");
        consumer.OnDone(usage: null, modelId: null);

        var assistant = Assert.Single(consumer.DrainEvents()
            .OfType<BridgeEvent.AssistantMessage>());
        Assert.Equal("# 标题\n    缩进代码块样式", assistant.Text);
        Assert.Null(assistant.Markdown);
    }

    /// <summary>命令回合结束自动复位 — 下一普通回合 Markdown 投影恢复（含语法字符文本投影非 null）。</summary>
    [Fact]
    public void OnDone_AfterCommandTurn_RestoresMarkdownForNextTurn()
    {
        var consumer = new InProcessEventConsumer();
        consumer.BeginCommandTurn();
        consumer.OnText("命令输出");
        consumer.OnDone(usage: null, modelId: null);

        consumer.OnText("**加粗** 回复");
        consumer.OnDone(usage: null, modelId: null);

        var events = consumer.DrainEvents().OfType<BridgeEvent.AssistantMessage>().ToList();
        Assert.Equal(2, events.Count);
        Assert.Null(events[0].Markdown);
        Assert.NotNull(events[1].Markdown);
    }

    /// <summary>Reset 兜底 — 命令回合异常中断（无 OnDone）时 Reset 清除纯文本模式标记。</summary>
    [Fact]
    public void Reset_ClearsCommandTurnMode()
    {
        var consumer = new InProcessEventConsumer();
        consumer.BeginCommandTurn();
        consumer.OnText("中途异常");

        consumer.Reset();
        consumer.OnText("*列表* 回复");
        consumer.OnDone(usage: null, modelId: null);

        var assistant = Assert.Single(consumer.DrainEvents()
            .OfType<BridgeEvent.AssistantMessage>());
        Assert.NotNull(assistant.Markdown);
    }

    /// <summary>对照面 — 未进入命令模式的普通回合照旧投影（既有行为无回归）。</summary>
    [Fact]
    public void NormalTurn_WithoutCommandMode_ProjectsMarkdownAsBefore()
    {
        var consumer = new InProcessEventConsumer();

        consumer.OnText("普通 **Markdown** 回复");
        consumer.OnDone(usage: null, modelId: null);

        var assistant = Assert.Single(consumer.DrainEvents()
            .OfType<BridgeEvent.AssistantMessage>());
        Assert.NotNull(assistant.Markdown);
    }

    /// <summary>端到端 — /help 命令输出经 DispatchSlashCommandAsync 上屏，Markdown 为 null（纯文本渲染）。</summary>
    [Fact]
    public async Task Dispatch_Help_CommandOutputRendersAsPlainText()
    {
        using var services = CommandTestHarness.BuildFakeServices(new CommandTestHarness.FakeChatService());
        var consumer = new InProcessEventConsumer();

        await EngineSession.DispatchSlashCommandAsync(services, "/help", consumer, new TuiCommandCallbacks(), CancellationToken.None);

        var events = consumer.DrainEvents().ToList();
        Assert.Contains(events, static e => e is BridgeEvent.TokenDelta);
        var assistant = Assert.Single(events.OfType<BridgeEvent.AssistantMessage>());
        Assert.True(assistant.Text.Length > 0);
        Assert.Null(assistant.Markdown);
    }
}
