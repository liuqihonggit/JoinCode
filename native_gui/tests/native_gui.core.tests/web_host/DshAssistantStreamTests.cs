namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// assistant-stream 瞬态帧测试（B13，ADR 0147）：hub 的 revision 密度/基线状态机，
/// 以及轮次执行器把引擎 delta 翻译成与持久 <c>assistant/message</c> 一致的 start/chunk/end。
/// </summary>
public sealed class DshAssistantStreamTests {
    private static JsonElement PromptArgs(string sessionId, string text) =>
        JsonSerializer.SerializeToElement(new {
            request = new {
                requestId = Guid.NewGuid().ToString("N"),
                sessionId,
                mode = "send",
                content = new[] { new { type = "text", text } },
            },
        });

    /// <summary>读一条 chunk 帧的 delta 类型与文本（帧的 chunk 是 JsonElement，读之前先收窄）</summary>
    private static (string Type, string Text) ReadChunk(DshAssistantStreamFrame frame) {
        if (frame.Chunk is not { } chunk) {
            throw new Xunit.Sdk.XunitException("chunk 帧缺 chunk 载荷（帧合成不完整）");
        }
        return (chunk.GetProperty("type").GetString() ?? "", chunk.GetProperty("text").GetString() ?? "");
    }

    /// <summary>按序读出全部 chunk 的 (类型, 文本)</summary>
    private static IEnumerable<(string Type, string Text)> Chunks(IReadOnlyList<DshAssistantStreamFrame> frames) =>
        frames.Where(frame => frame.Type == "chunk").Select(ReadChunk);

    /// <summary>帧 revision 必须严格 +1（前端强校验），基线在尝试进行/结束后如实反映状态。</summary>
    [Fact]
    public async Task AssistantStream_RevisionDenseAndBaselineTracksActiveAttempt() {
        var hub = new DshSessionStreamHub();
        var frames = new List<DshAssistantStreamFrame>();
        var baseline = hub.AssistantBaseline("s1");
        baseline.Revision.Should().Be(0, "空闲会话没有历史帧");
        baseline.ActiveAttempt.Should().BeNull();
        var (_, lease) = hub.SubscribeSession("s1", "st1", _ => Task.CompletedTask, frame => {
            frames.Add(frame);
            return Task.CompletedTask;
        }, -1);
        using (lease) {
            await hub.ActivateAsync("st1");

            await hub.StartAssistantAttemptAsync("s1", turn: 2, step: 1);
            await hub.AppendAssistantChunkAsync("s1", "text-delta", 1, "你");
            await hub.AppendAssistantChunkAsync("s1", "reasoning-delta", 0, "想");

            var live = hub.AssistantBaseline("s1");
            live.Revision.Should().Be(3);
            if (live.ActiveAttempt is not { } attempt) {
                throw new Xunit.Sdk.XunitException("进行中的尝试必须出现在基线里（重连重建未完成文本）");
            }
            attempt.NextIndex.Should().Be(2);
            attempt.Turn.Should().Be(2);
            attempt.Step.Should().Be(1);
            attempt.StartedAfterSeq.Should().Be(-1);
            attempt.Stream.Should().HaveCount(2);
            attempt.Stream[0].Chunk.GetProperty("type").GetString().Should().Be("text-delta");
            attempt.Stream[0].Chunk.GetProperty("text").GetString().Should().Be("你");

            await hub.EndAssistantAttemptAsync("s1", new DshAssistantStreamOutcome { Kind = "committed", EventType = "assistant/message", Seq = 7 });

            hub.AssistantBaseline("s1").Revision.Should().Be(4);
            hub.AssistantBaseline("s1").ActiveAttempt.Should().BeNull("结算后不再有进行中的尝试");
        }
        frames.Select(frame => frame.Revision).Should().Equal(1, 2, 3, 4);
        frames.Select(frame => frame.Type).Should().Equal("start", "chunk", "chunk", "end");
        frames[0].Turn.Should().Be(2);
        if (frames[3].Outcome is not { } outcome) {
            throw new Xunit.Sdk.XunitException("end 帧缺 outcome");
        }
        outcome.Kind.Should().Be("committed");
        outcome.Seq.Should().Be(7);
        frames[3].Index.Should().Be(2, "end.index 必须等于下一条 chunk 序号（前端据此判定连续）");
    }

    /// <summary>跨轮次 revision 不回退（重连基线 + 密度校验都依赖单调递增）。</summary>
    [Fact]
    public async Task AssistantStream_SecondAttemptContinuesRevision() {
        var hub = new DshSessionStreamHub();
        await hub.StartAssistantAttemptAsync("s1", 1, 1);
        await hub.EndAssistantAttemptAsync("s1", new DshAssistantStreamOutcome { Kind = "abandoned" });
        var afterFirst = hub.AssistantBaseline("s1").Revision;

        await hub.StartAssistantAttemptAsync("s1", 2, 1);

        hub.AssistantBaseline("s1").Revision.Should().Be(afterFirst + 1);
        hub.AssistantBaseline("s1").ActiveAttempt.Should().NotBeNull();
    }

    /// <summary>轮次执行器：逐字帧拼接结果必须与持久助手消息的块内容逐字一致，end.seq 指向该事件。</summary>
    [Fact]
    public async Task TurnRunner_StreamsDeltasConsistentWithDurableMessage() {
        await using var engine = new PlaceholderChatSession();
        var (probe, dispatcher, frames) = await StartSessionAsync(engine);

        (await dispatcher.HandleAsync("session/prompt", PromptArgs(probe.SessionId, "流式验证"))).Ok.Should().BeTrue();
        await probe.TurnEnded.Task.WaitAsync(TimeSpan.FromSeconds(20));

        var snapshot = frames.ToArray();
        snapshot.Should().NotBeEmpty("引擎有思考/正文 delta，必须产生瞬态帧");
        snapshot[0].Type.Should().Be("start");
        snapshot[^1].Type.Should().Be("end");
        if (snapshot[^1].Outcome is not { } outcome) {
            throw new Xunit.Sdk.XunitException("末帧必须是带 outcome 的 end");
        }
        outcome.Kind.Should().Be("committed");
        snapshot.Select(frame => frame.Revision).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
        snapshot[^1].Index.Should().Be(snapshot.Count(frame => frame.Type == "chunk"), "end.index = chunk 帧总数");

        var messageEvent = probe.Services.Streams.Events(probe.SessionId).Single(sessionEvent => sessionEvent.Type == "assistant/message");
        outcome.Seq.Should().Be(messageEvent.Seq);
        var blocks = messageEvent.Data.GetProperty("message").GetProperty("content").EnumerateArray().ToArray();
        var chunks = Chunks(snapshot).ToArray();

        var durableText = string.Concat(blocks.Where(block => block.GetProperty("type").GetString() == "text")
            .Select(block => block.GetProperty("text").GetString()));
        string.Concat(chunks.Where(chunk => chunk.Type == "text-delta").Select(chunk => chunk.Text))
            .Should().Be(durableText, "瞬态流与持久结算是同一份内容，前端替换时不得跳字");

        var durableReasoning = string.Concat(blocks.Where(block => block.GetProperty("type").GetString() == "reasoning")
            .Select(block => block.GetProperty("text").GetString()));
        string.Concat(chunks.Where(chunk => chunk.Type == "reasoning-delta").Select(chunk => chunk.Text))
            .Should().Be(durableReasoning);
        chunks.Length.Should().BeGreaterThan(100, "占位引擎逐字符产出，帧数应与之匹配");
    }

    /// <summary>纯正文轮（无思考块）的正文下标必须是 0：留空洞会让前端块数组首元素缺失，流式正文不渲染。</summary>
    [Fact]
    public async Task TurnRunner_TextOnlyAttemptStartsAtBlockZero() {
        await using var engine = new PlaceholderChatSession { EmitThinking = false };
        var (probe, dispatcher, frames) = await StartSessionAsync(engine);

        (await dispatcher.HandleAsync("session/prompt", PromptArgs(probe.SessionId, "纯正文"))).Ok.Should().BeTrue();
        await probe.TurnEnded.Task.WaitAsync(TimeSpan.FromSeconds(20));

        var indexes = frames.Where(frame => frame.Type == "chunk")
            .Select(frame => frame.Chunk is { } chunk ? chunk.GetProperty("index").GetInt32() : -1)
            .Distinct().ToArray();
        indexes.Should().BeEquivalentTo([0], "块下标按内容块出现顺序分配，首块恒为 0");
    }

    /// <summary>瞬态帧不落会话日志（重放以 assistant/message 为准）。</summary>
    [Fact]
    public async Task TurnRunner_AssistantFramesAreNotJournaled() {
        await using var engine = new PlaceholderChatSession();
        var (probe, dispatcher, _) = await StartSessionAsync(engine);

        (await dispatcher.HandleAsync("session/prompt", PromptArgs(probe.SessionId, "不落盘"))).Ok.Should().BeTrue();
        await probe.TurnEnded.Task.WaitAsync(TimeSpan.FromSeconds(20));

        probe.Services.Streams.Events(probe.SessionId)
            .Should().OnlyContain(sessionEvent => sessionEvent.Type != "assistant-stream",
                "瞬态帧不进日志，否则历史投影会重复渲染半截消息");
    }

    /// <summary>建会话 + 订阅瞬态帧（订阅者本身就是被测通道的消费点）</summary>
    private static async Task<(SessionProbe Probe, DshRpcDispatcher Dispatcher, ConcurrentQueue<DshAssistantStreamFrame> Frames)> StartSessionAsync(PlaceholderChatSession engine) {
        var fs = new InMemoryFileSystem();
        var services = new DshHostServices(fs);
        await using var transcripts = new State.TranscriptService(fs, "/test/sessions");
        services.Sessions.AttachTranscripts(transcripts);
        services.AttachEngine(engine);
        var created = await services.Sessions.CreateAsync(null, "/test/proj", "deepseek", "deepseek-v4-pro", CancellationToken.None);
        var sessionId = (created ?? throw new Xunit.Sdk.XunitException("session/create 失败")).SessionId;
        var frames = new ConcurrentQueue<DshAssistantStreamFrame>();
        var turnEnded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (_, lease) = services.Streams.SubscribeSession(
            sessionId,
            "probe",
            sessionEvent => {
                if (sessionEvent.Type == "turn/end") {
                    turnEnded.TrySetResult();
                }
                return Task.CompletedTask;
            },
            frame => {
                frames.Enqueue(frame);
                return Task.CompletedTask;
            },
            -1);
        _ = lease;
        await services.Streams.ActivateAsync("probe");
        return (new SessionProbe(services, sessionId, turnEnded), new DshRpcDispatcher(services), frames);
    }

    /// <summary>测试夹具：宿主服务 + 会话身份 + 轮次收尾信号</summary>
    private sealed record SessionProbe(DshHostServices Services, string SessionId, TaskCompletionSource TurnEnded);
}
