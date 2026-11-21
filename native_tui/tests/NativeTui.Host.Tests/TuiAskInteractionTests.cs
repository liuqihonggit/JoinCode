namespace NativeTui.Host.Tests;

/// <summary>
/// TASK103 S14 原生 TUI 问答链路测试 — TuiAskBridge（未绑定/空问题集 Failure 不挂起，通知探测透传）+
/// TuiInteractiveService（单问/多问映射：Success/MultiSelect/QuestionsResult/Cancelled/Deferred 部分答案/Failure，
/// 选项不足诱导）— 对齐 CLI TerminalInteractiveService 与 Core 自动应答占位的替换面。
/// </summary>
public sealed class TuiAskInteractionTests {
    /// <summary>固定结果假 bridge — 捕获收到的 TuiAskQuestion 集。</summary>
    private sealed class FakeBridge(TuiAskResult result) : TuiAskBridge {
        /// <summary>最后一次 Ask 收到的问题集。</summary>
        public IReadOnlyList<TuiAskQuestion>? LastQuestions { get; private set; }

        /// <summary>返回固定结果并记录问题集。</summary>
        public override TuiAskResult Ask(IReadOnlyList<TuiAskQuestion> questions) {
            LastQuestions = questions;
            return result;
        }
    }

    /// <summary>待处理通知队列假实现。</summary>
    private sealed class FakeNotificationQueue(bool pending) : IAgentNotificationQueue {
        /// <summary>入队不参与本测试链路。</summary>
        public void Enqueue(string? parentAgentId, string notificationXml) {
        }

        /// <summary>无出队需求。</summary>
        public IReadOnlyList<QueuedNotification> DequeueAll(string? currentAgentId = null) => [];

        /// <summary>固定待处理标记。</summary>
        public bool HasPendingNotifications => pending;
    }

    // ---- TuiAskBridge ----

    /// <summary>未绑定 shell — Failure 携带恢复诱导（不阻塞引擎线程）。</summary>
    [Fact]
    public void Bridge_Unbound_ReturnsFailureWithGuidance() {
        var bridge = new TuiAskBridge();

        var result = bridge.Ask([new TuiAskQuestion("Q", "H", [new("A", ""), new("B", "")], false)]);

        Assert.False(result.Succeeded);
        Assert.False(result.Cancelled);
        Assert.False(result.Deferred);
        Assert.Contains("稍后重新提问", result.Error);
    }

    /// <summary>空问题集 — Failure（不进入挂起等待）。</summary>
    [Fact]
    public void Bridge_EmptyQuestions_ReturnsFailure() {
        var bridge = new TuiAskBridge();

        var result = bridge.Ask([]);

        Assert.False(result.Succeeded);
        Assert.Contains("没有可提问的问题", result.Error);
    }

    /// <summary>通知探测 — 无队列恒 false；有队列透传 HasPendingNotifications。</summary>
    [Fact]
    public void Bridge_NotificationProbe_TracksQueue() {
        var bridge = new TuiAskBridge();
        Assert.False(bridge.HasPendingNotification);

        bridge.AttachNotificationQueue(new FakeNotificationQueue(pending: true));
        Assert.True(bridge.HasPendingNotification);

        bridge.AttachNotificationQueue(new FakeNotificationQueue(pending: false));
        Assert.False(bridge.HasPendingNotification);
    }

    // ---- TuiInteractiveService ----

    /// <summary>选项不足 2 个 — Failure 诱导（TUI 对话框无法呈现），不挂起。</summary>
    [Fact]
    public async Task Service_TooFewOptions_ReturnsFailure() {
        var service = new TuiInteractiveService(new FakeBridge(TuiAskResult.CancelledResult));

        var result = await service.AskUserQuestionAsync("Q", options: ["only"]);

        Assert.False(result.Success);
        Assert.Contains("至少需要 2 个选项", result.ErrorMessage);
    }

    /// <summary>单选成功 — 映射 SuccessResult 且问题/选项透传到对话框。</summary>
    [Fact]
    public async Task Service_Single_MapsAnswer() {
        var bridge = new FakeBridge(TuiAskResult.Success(new Dictionary<string, string> { ["选哪个？"] = "B" }));
        var service = new TuiInteractiveService(bridge);

        var result = await service.AskUserQuestionAsync("选哪个？", options: ["A", "B"]);

        Assert.True(result.Success);
        Assert.Equal("B", result.Answer);
        Assert.Equal("选哪个？", bridge.LastQuestions?[0].Question);
        Assert.Equal(["A", "B"], bridge.LastQuestions?[0].Options.Select(o => o.Label));
        Assert.False(bridge.LastQuestions?[0].MultiSelect);
    }

    /// <summary>多选成功 — 连接串拆回标签列表。</summary>
    [Fact]
    public async Task Service_MultiSelect_SplitsLabels() {
        var bridge = new FakeBridge(TuiAskResult.Success(new Dictionary<string, string> { ["Q"] = "A, C" }));
        var service = new TuiInteractiveService(bridge);

        var result = await service.AskUserQuestionAsync("Q", options: ["A", "B", "C"], multiSelect: true);

        Assert.True(result.Success);
        Assert.Equal(["A", "C"], result.SelectedOptions);
        Assert.True(bridge.LastQuestions?[0].MultiSelect);
    }

    /// <summary>取消 — 映射 CancelledResult。</summary>
    [Fact]
    public async Task Service_Cancelled_MapsCancelled() {
        var service = new TuiInteractiveService(new FakeBridge(TuiAskResult.CancelledResult));

        var result = await service.AskUserQuestionAsync("Q", options: ["A", "B"]);

        Assert.True(result.Cancelled);
    }

    /// <summary>后台回调 Defer — 映射 DeferredResult 并携带部分答案。</summary>
    [Fact]
    public async Task Service_Deferred_CarriesPartialAnswers() {
        var bridge = new FakeBridge(TuiAskResult.DeferredResult(new Dictionary<string, string> { ["Q"] = "A" }));
        var service = new TuiInteractiveService(bridge);

        var result = await service.AskUserQuestionAsync("Q", options: ["A", "B"]);

        Assert.True(result.Deferred);
        Assert.Equal("A", result.PartialAnswers?["Q"]);
    }

    /// <summary>失败 — 错误消息透传（追加重新提问诱导）。</summary>
    [Fact]
    public async Task Service_Failure_PassesError() {
        var service = new TuiInteractiveService(new FakeBridge(TuiAskResult.Failure("界面已退出")));

        var result = await service.AskUserQuestionAsync("Q", options: ["A", "B"]);

        Assert.False(result.Success);
        Assert.Contains("界面已退出", result.ErrorMessage);
    }

    /// <summary>多问题成功 — 映射 QuestionsResult 答案字典；Header/MultiSelect 透传。</summary>
    [Fact]
    public async Task Service_Questions_MapsAnswersAndMetadata() {
        var answers = new Dictionary<string, string> { ["Q1"] = "A", ["Q2"] = "X" };
        var bridge = new FakeBridge(TuiAskResult.Success(answers));
        var service = new TuiInteractiveService(bridge);
        var questions = new List<QuestionItem> {
            new() { Question = "Q1", Header = "H1", Options = [new() { Label = "A", Description = "a" }, new() { Label = "B", Description = "b" }] },
            new() { Question = "Q2", Header = "H2", Options = [new() { Label = "X", Description = "x" }, new() { Label = "Y", Description = "y" }], MultiSelect = true },
        };

        var result = await service.AskUserQuestionsAsync(questions);

        Assert.True(result.Success);
        Assert.Equal("A", result.Answers?["Q1"]);
        Assert.Equal("X", result.Answers?["Q2"]);
        Assert.Equal("H1", bridge.LastQuestions?[0].Header);
        Assert.True(bridge.LastQuestions?[1].MultiSelect);
    }

    /// <summary>空问题集 — Failure（不挂起）。</summary>
    [Fact]
    public async Task Service_QuestionsEmpty_ReturnsFailure() {
        var service = new TuiInteractiveService(new FakeBridge(TuiAskResult.Success(new Dictionary<string, string>())));

        var result = await service.AskUserQuestionsAsync([]);

        Assert.False(result.Success);
        Assert.Contains("No questions", result.ErrorMessage);
    }
}
