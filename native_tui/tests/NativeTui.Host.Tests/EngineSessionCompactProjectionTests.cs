namespace NativeTui.Host.Tests;

/// <summary>
/// TASK070 C6/DSG046 — /compact 手动压缩事件投影测试：
/// EngineSession.DispatchSlashCommandAsync 消费 ChatCommandResult.Data（CompactSummaryData）
/// → consumer.OnCompactCompleted(isAuto:false) → InProcessEventConsumer 队列 CompactCompleted 事件
/// （ReplBridgeDriver 侧 TokenUsageTracker.Reset 消费由 NativeTui.Tests 覆盖，此处验宿主投影链）。
/// </summary>
[Collection(SlashCommandSerialCollection.CollectionName)]
public sealed class EngineSessionCompactProjectionTests
{
    [Fact]
    public async Task DispatchCompact_EmitsCompactCompletedBeforeTurnDone()
    {
        var chat = new CommandTestHarness.CompactChatService();
        using var services = CommandTestHarness.BuildFakeServices(chat);
        var callbacks = new TuiCommandCallbacks();
        var consumer = new InProcessEventConsumer();

        await EngineSession.DispatchSlashCommandAsync(services, "/compact", consumer, callbacks, CancellationToken.None);

        var events = consumer.DrainEvents();
        var completed = events.OfType<BridgeEvent.CompactCompleted>().ToArray();
        Assert.Single(completed);
        var expectedCompressed = global::Core.Prompts.Templates.Memory.CompactPromptTemplate
            .FormatCompactSummary("稳定摘要文本").Length / 4;
        // OriginalTokens 口径 = CompactCommand.CalculateOriginalMetrics（总字符数/4：6 字符 → 1）
        var expectedOriginal = ("第一问" + "第一答").Length / 4;
        Assert.Equal((2, (long)expectedOriginal, (long)expectedCompressed, false),
            (completed[0].MessagesSummarized, completed[0].OriginalTokens, completed[0].CompressedTokens, completed[0].IsAuto));
        Assert.Equal(1, chat.CompactHistoryCalls);

        // 事件序 — compact_completed 先于命令回合 OnDone 产出的 assistant_message 到达
        var compactIndex = events.ToList().FindIndex(e => e is BridgeEvent.CompactCompleted);
        var doneIndex = events.ToList().FindIndex(e => e is BridgeEvent.AssistantMessage);
        Assert.True(compactIndex < doneIndex, "compact_completed 应先于命令回合 OnDone 产出的 assistant_message 到达");
    }

    [Fact]
    public async Task DispatchNonCompactCommand_NoCompactEvent()
    {
        var chat = new CommandTestHarness.CompactChatService();
        using var services = CommandTestHarness.BuildFakeServices(chat);
        var callbacks = new TuiCommandCallbacks();
        var consumer = new InProcessEventConsumer();

        await EngineSession.DispatchSlashCommandAsync(services, "/clear", consumer, callbacks, CancellationToken.None);

        Assert.DoesNotContain(consumer.DrainEvents(), e => e is BridgeEvent.CompactCompleted);
        Assert.Equal(0, chat.CompactHistoryCalls);
    }
}
