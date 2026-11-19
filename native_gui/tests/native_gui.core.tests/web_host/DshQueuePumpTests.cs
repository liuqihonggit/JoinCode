namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// mode=queue 入队与轮次收尾的竞态回归（CI 实证：gui-core 的
/// <see cref="DshGoalsTests.Goals_RoundsStarted_IncrementedByTurnAcceptance"/> 偶发 5s 超时）。
/// <para>
/// 竞态：请求线程「判定 busy」之后、真正入队之前，本轮刚好收尾 —— 收尾侧的 StartQueued
/// 已经拉过一遍空队列，于是这一项永远不会被拉起（现象：排队输入消失，直到下一条输入才被带出）。
/// 这里用 <see cref="DshTurnRunner.QueueEnqueueGate"/> 把该窗口固定下来，属于确定性复现，
/// 不靠 sleep 撞时序。
/// </para>
/// </summary>
public sealed class DshQueuePumpTests {
    private static DshSessionPromptArgs Prompt(string sessionId, string text, string mode) => new() {
        RequestId = Guid.NewGuid().ToString("N"),
        SessionId = sessionId,
        Mode = mode,
        Content = [new DshPromptContent { Type = "text", Text = text }],
    };

    [Fact]
    public async Task QueueEnqueuedWhileTurnFinishes_IsStartedInsteadOfOrphaned() {
        var fs = new InMemoryFileSystem();
        var services = new DshHostServices(fs);
        await using var transcripts = new State.TranscriptService(fs, "/test/sessions");
        services.Sessions.AttachTranscripts(transcripts);
        await using var engine = new PlaceholderChatSession();
        services.AttachEngine(engine);
        var runner = new DshTurnRunner(services);

        var created = await services.Sessions.CreateAsync(null, "/test/proj", "deepseek", "deepseek-v4-pro", CancellationToken.None);
        var sessionId = (created ?? throw new Xunit.Sdk.XunitException("session/create 失败")).SessionId;

        // 手工占用第 1 轮：收尾只由本测试驱动，避免真实生成引入额外时序。
        services.Streams.TryBeginTurn(sessionId).HasValue.Should().BeTrue();

        var atGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        runner.QueueEnqueueGate = () => {
            atGate.TrySetResult();
            return release.Task;
        };

        var turnStarted = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var subscription = services.Streams.Subscribe(sessionId, "queue-pump-test", sessionEvent => {
            if (sessionEvent.Type == "turn/start"
                && sessionEvent.Data.TryGetProperty("turn", out var turn)
                && turn.TryGetInt32(out var number)) {
                turnStarted.TrySetResult(number);
            }
            return Task.CompletedTask;
        });

        // 判定 busy 后卡在闸门（此时尚未入队）。
        var queued = runner.StartAsync(Prompt(sessionId, "排队输入", "queue"), CancellationToken.None);
        await atGate.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // 本轮收尾：真实路径里收尾侧会 StartQueued 拉一遍空队列 —— 这里显式走到同一状态。
        services.Streams.EndTurn(sessionId);

        // 放行入队：修复前无人再拉（孤儿），修复后入队即自拉，立刻开第 2 轮。
        release.TrySetResult();
        var accepted = await queued.WaitAsync(TimeSpan.FromSeconds(5));
        accepted.HasValue.Should().BeFalse($"受理失败: {accepted?.Code}: {accepted?.Message}");

        (await turnStarted.Task.WaitAsync(TimeSpan.FromSeconds(5))).Should().Be(2);
        runner.ListQueue(sessionId).Should().BeEmpty("排队项已出队开跑，不应还留在队列里");
    }
}
