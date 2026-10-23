namespace JoinCode.Gui.Tests.WebHost;

/// <summary>
/// AskUserQuestion 卡片桥测试（TASK066 C7）：引擎 ask_user_question → 宿主 $events
/// <c>user-questions/request</c> waterfall 帧 → 前端问题卡片 → <c>$events/result</c> 回帖 → 引擎结果。
/// 断言帧形（只含 type/event/eventId/agentId/request，request 禁 agent/signal）、
/// 答案映射（多选/自定义/跳过）、无应答者 fail-closed，以及轮次收尾/断流时挂起请求被取消
/// 并补发 <c>cancel</c> 帧（对齐 DSH gateway finishRemoteEvent，否则前端卡片永远等待）。
/// </summary>
public sealed class DshUserQuestionsTests {
    private static QuestionItem Question(string text, string header, params string[] options) => new() {
        Question = text,
        Header = header,
        Options = [.. options.Select(label => new QuestionOption { Label = label, Description = label + " 的说明" })],
    };

    private static JsonElement AnswerBatch(params (string Id, string[] Selected, string? Custom)[] answers) =>
        JsonSerializer.SerializeToElement(new {
            answers = answers.Select(answer => new {
                id = answer.Id,
                selected = answer.Selected,
                custom = answer.Custom,
            }).ToArray(),
        });

    /// <summary>帧收集器：发送委托投递帧并释放信号，测试按序号等待，不盲等时间</summary>
    private sealed class FrameFeed {
        private readonly SemaphoreSlim _signal = new(0);
        private readonly ConcurrentQueue<JsonElement> _frames = new();

        internal Task Add(JsonElement frame) {
            _frames.Enqueue(frame.Clone());
            _signal.Release();
            return Task.CompletedTask;
        }

        internal IReadOnlyList<JsonElement> Snapshot() => [.. _frames];

        internal async Task<JsonElement> AtAsync(int index) {
            while (true) {
                var frames = Snapshot();
                if (frames.Count > index) {
                    return frames[index];
                }
                if (!await _signal.WaitAsync(TimeSpan.FromSeconds(5))) {
                    throw new Xunit.Sdk.XunitException($"等待第 {index} 帧超时，已收到 {frames.Count} 帧");
                }
            }
        }
    }

    private static string EventIdOf(JsonElement frame) =>
        frame.GetProperty("eventId").GetString()
        ?? throw new Xunit.Sdk.XunitException("waterfall 帧缺少 eventId");

    private static Dictionary<string, string> AnswersOf(AskUserQuestionResult result) =>
        result.Answers ?? throw new Xunit.Sdk.XunitException("期望 Answers 字典存在");

    private static (DshUserQuestionBroker Broker, FrameFeed Frames) CreateBroker() {
        var frames = new FrameFeed();
        var broker = new DshUserQuestionBroker();
        broker.Attach(frames.Add, "client-q");
        broker.SetAgent("s-ask");
        return (broker, frames);
    }

    [Fact]
    public async Task Request_SendsWaterfallFrame_AndMapsAnswerBatch() {
        var (broker, frames) = CreateBroker();
        var pending = broker.RequestAsync([
            Question("继续吗？", "确认", "继续", "停止"),
            Question("选哪些？", "多选", "A", "B", "C"),
        ], CancellationToken.None);

        var frame = await frames.AtAsync(0);
        frame.GetProperty("type").GetString().Should().Be("waterfall");
        frame.GetProperty("event").GetString().Should().Be("user-questions/request");
        frame.GetProperty("agentId").GetString().Should().Be("s-ask");
        var frameKeys = frame.EnumerateObject().Select(property => property.Name)
            .OrderBy(name => name, StringComparer.Ordinal).ToArray();
        frameKeys.Should().Equal(["agentId", "event", "eventId", "request", "type"],
            "前端 parseRemoteEventFrame 硬校验五键");

        var request = frame.GetProperty("request");
        request.TryGetProperty("agent", out _).Should().BeFalse("gateway 会自行注入 agent，帧载荷带 agent 会被拒");
        request.TryGetProperty("signal", out _).Should().BeFalse();
        request.TryGetProperty("wait", out _).Should().BeFalse("引擎 ask_user_question 是阻塞式，不带计时等待");
        var questions = request.GetProperty("questions");
        questions.GetArrayLength().Should().Be(2);
        questions[0].GetProperty("id").GetString().Should().Be("q0");
        questions[0].GetProperty("header").GetString().Should().Be("确认");
        questions[0].GetProperty("options").GetArrayLength().Should().Be(2);
        questions[0].GetProperty("options")[1].GetProperty("label").GetString().Should().Be("停止");
        questions[1].GetProperty("multiSelect").GetBoolean().Should().BeFalse();

        var eventId = EventIdOf(frame);
        var settled = await broker.SettleAsync("client-q", eventId, new DshEventOutcome {
            Kind = "result",
            Value = AnswerBatch(("q0", ["停止"], null), ("q1", ["A", "B"], null)),
        });
        settled.Should().BeTrue();

        var result = await pending;
        result.Success.Should().BeTrue();
        AnswersOf(result)["继续吗？"].Should().Be("停止");
        AnswersOf(result)["选哪些？"].Should().Be("A, B", "多选按选择顺序逗号连接");

        var cancel = await frames.AtAsync(1);
        cancel.GetProperty("type").GetString().Should().Be("cancel", "结算后必须让前端停止等待该 waterfall");
        cancel.GetProperty("eventId").GetString().Should().Be(eventId);
        frames.Snapshot().Should().HaveCount(2);
    }

    [Fact]
    public async Task Answer_CustomText_AppendsAfterSelected() {
        var (broker, frames) = CreateBroker();
        var pending = broker.RequestAsync([Question("方案？", "方案", "A", "B")], CancellationToken.None);
        var frame = await frames.AtAsync(0);

        await broker.SettleAsync("client-q", EventIdOf(frame), new DshEventOutcome {
            Kind = "result",
            Value = AnswerBatch(("q0", ["A"], "自定义说明")),
        });

        AnswersOf(await pending)["方案？"].Should().Be("A, 自定义说明");
    }

    [Fact]
    public async Task Answer_SkippedQuestion_YieldsEmptyValue() {
        var (broker, frames) = CreateBroker();
        var pending = broker.RequestAsync([Question("做吗？", "确认", "做", "不做")], CancellationToken.None);
        var frame = await frames.AtAsync(0);

        await broker.SettleAsync("client-q", EventIdOf(frame), new DshEventOutcome {
            Kind = "result",
            Value = AnswerBatch(("q0", [], null)),
        });

        var result = await pending;
        result.Success.Should().BeTrue("跳过是已提交的答案，不是取消");
        AnswersOf(result)["做吗？"].Should().BeEmpty();
    }

    [Fact]
    public async Task Settle_NextOrRejected_CancelsRequest() {
        var (broker, frames) = CreateBroker();
        var pending = broker.RequestAsync([Question("问？", "问", "是", "否")], CancellationToken.None);
        var frame = await frames.AtAsync(0);

        (await broker.SettleAsync("client-q", EventIdOf(frame), new DshEventOutcome { Kind = "next" }))
            .Should().BeTrue();

        var result = await pending;
        result.Success.Should().BeFalse();
        result.Cancelled.Should().BeTrue("无人处理/监听器抛错一律按取消结算");
    }

    [Fact]
    public async Task Settle_StaleClient_IsIgnored() {
        var (broker, frames) = CreateBroker();
        var pending = broker.RequestAsync([Question("问？", "问", "是", "否")], CancellationToken.None);
        var frame = await frames.AtAsync(0);

        (await broker.SettleAsync("old-client", EventIdOf(frame), new DshEventOutcome {
            Kind = "result",
            Value = AnswerBatch(("q0", ["是"], null)),
        })).Should().BeFalse("重连后的旧代际回帖必须幂等忽略");

        pending.IsCompleted.Should().BeFalse();
        await broker.CloseTurnAsync("s-ask");
        (await pending).Cancelled.Should().BeTrue();
    }

    [Fact]
    public async Task Detach_CancelsPending_AndRejectsLaterRequests() {
        var (broker, frames) = CreateBroker();
        var pending = broker.RequestAsync([Question("问？", "问", "是", "否")], CancellationToken.None);
        await frames.AtAsync(0);

        broker.Detach();
        (await pending).Cancelled.Should().BeTrue();

        var later = await broker.RequestAsync([Question("问？", "问", "是", "否")], CancellationToken.None);
        later.Success.Should().BeFalse();
        later.ErrorMessage.Should().Contain("前端", "无应答者时给出可操作引导");
    }

    [Fact]
    public async Task CloseTurn_CancelsOnlyThatSession_AndSendsCancelFrame() {
        var frames = new FrameFeed();
        var broker = new DshUserQuestionBroker();
        broker.Attach(frames.Add, "client-q");

        broker.SetAgent("s-one");
        var first = broker.RequestAsync([Question("一？", "一", "是", "否")], CancellationToken.None);
        await frames.AtAsync(0);
        broker.SetAgent("s-two");
        var second = broker.RequestAsync([Question("二？", "二", "是", "否")], CancellationToken.None);
        await frames.AtAsync(1);

        await broker.CloseTurnAsync("s-one");
        (await first).Cancelled.Should().BeTrue("轮次结束必须结算本会话挂起提问");
        second.IsCompleted.Should().BeFalse("其它会话的挂起提问不受影响");
        frames.Snapshot().Count(frame => frame.GetProperty("type").GetString() == "cancel").Should().Be(1);

        await broker.CloseTurnAsync("s-two");
        (await second).Cancelled.Should().BeTrue();
    }

    [Fact]
    public async Task EventsResult_SettlesQuestionBridge_ThroughDispatcher() {
        var services = new DshHostServices(new InMemoryFileSystem());
        var dispatcher = new DshRpcDispatcher(services);
        var frames = new FrameFeed();
        services.Questions.Attach(frames.Add, "client-d");
        services.Questions.SetAgent("s-dispatch");

        var pending = services.Questions.RequestAsync([Question("走 RPC？", "确认", "是", "否")], CancellationToken.None);
        var frame = await frames.AtAsync(0);
        var reply = await dispatcher.HandleAsync("$events/result", JsonSerializer.SerializeToElement(new {
            clientId = "client-d",
            eventId = EventIdOf(frame),
            outcome = new {
                kind = "result",
                value = new { answers = new[] { new { id = "q0", selected = new[] { "是" } } } },
            },
        }));

        reply.Ok.Should().BeTrue(reply.Error?.Message);
        AnswersOf(await pending)["走 RPC？"].Should().Be("是");
    }

    [Fact]
    public async Task UserQuestionsAnswer_ReturnsFalse_BecauseHostHasNoContinuedQueue() {
        var services = new DshHostServices(new InMemoryFileSystem());
        var dispatcher = new DshRpcDispatcher(services);

        var reply = await dispatcher.HandleAsync("userQuestions/answer", JsonSerializer.SerializeToElement(new {
            agentId = "s-answer",
            callId = "call-1",
            answer = new { answers = Array.Empty<object>() },
        }));

        reply.Ok.Should().BeTrue(reply.Error?.Message);
        var value = reply.Value ?? throw new Xunit.Sdk.XunitException("期望 userQuestions/answer 返回布尔值");
        value.GetBoolean().Should().BeFalse(
            "宿主 ask_user_question 是阻塞式调用，不存在 DSH 的 continued 队列；false = 该问题已不可续答");
    }

    [Fact]
    public async Task UserQuestionsAnswer_MissingArgs_ReturnsGuidance() {
        var services = new DshHostServices(new InMemoryFileSystem());
        var dispatcher = new DshRpcDispatcher(services);

        var reply = await dispatcher.HandleAsync("userQuestions/answer", JsonSerializer.SerializeToElement(new { agentId = "s" }));

        reply.Ok.Should().BeFalse();
        var error = reply.Error ?? throw new Xunit.Sdk.XunitException("期望参数缺失返回错误");
        error.Message.Should().Contain("callId").And.Contain("answer");
    }
}
