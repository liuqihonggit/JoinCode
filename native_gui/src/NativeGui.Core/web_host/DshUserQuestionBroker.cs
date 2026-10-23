namespace JoinCode.Gui.WebHost;

/// <summary>
/// DSH 提问卡片桥（TASK066 C7）：引擎 <c>ask_user_question</c> → <c>$events</c> 流发
/// <c>user-questions/request</c> waterfall 帧 → 前端问题卡片 → <c>POST /api/$events/result</c> 回帖 → 引擎结果。
/// 宿主侧 ask 是阻塞式（无 DSH 的 timed wait），因此不回 <c>wait</c> 字段，也不走 <c>userQuestions/answer</c> 队列。
/// 每次结算/取消都补发 <c>{type:"cancel",eventId}</c>（对齐 DSH gateway finishRemoteEvent），
/// 否则前端卡片会一直等待该 waterfall。无应答者一律 fail-closed 返回取消。
/// </summary>
internal sealed class DshUserQuestionBroker {
    /// <summary>一次挂起中的提问（eventId 回帖结算，Questions 用于答案回填与结果映射）</summary>
    private sealed record Pending(
        string EventId,
        string? SessionId,
        IReadOnlyList<QuestionItem> Questions,
        TaskCompletionSource<AskUserQuestionResult> Completion);

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, Pending> _pending = new(StringComparer.Ordinal);

    /// <summary>当前 $events 连接的发送通道（null = 前端未连接/已断开）</summary>
    private (Func<JsonElement, Task> Send, string ClientId)? _events;

    /// <summary>waterfall 帧的目标会话（DshTurnRunner 轮次开始时设置）</summary>
    private volatile string? _agentId;

    /// <summary>连接状态读写锁（保护 _events 与结算一致性）</summary>
    private readonly object _gate = new();

    /// <summary>$events 流打开时挂接发送通道（连接级单例；旧连接被新连接替换）</summary>
    /// <param name="send">帧值发送委托（宿主闭包绑定 streamId）</param>
    /// <param name="clientId">就绪帧下发的客户端标识</param>
    internal void Attach(Func<JsonElement, Task> send, string clientId) {
        lock (_gate) { _events = (send, clientId); }
        GuiDiag.Log($"[DSH-QUESTION] events stream attached client={clientId}");
    }

    /// <summary>$events 流关闭时摘除通道：挂起中的提问立即取消，后续提问直接失败（fail-closed）</summary>
    internal void Detach() {
        lock (_gate) { _events = null; }
        foreach (var (eventId, pending) in _pending) {
            if (_pending.TryRemove(eventId, out var removed)) {
                removed.Completion.TrySetResult(AskUserQuestionResult.CancelledResult());
            }
        }
        GuiDiag.Log("[DSH-QUESTION] events stream detached: pending questions cancelled");
    }

    /// <summary>设置 waterfall 目标会话（前端按 agentId 路由到对应会话 scope）</summary>
    internal void SetAgent(string? sessionId) => _agentId = sessionId;

    /// <summary>
    /// 轮次收尾：取消该会话仍挂起的提问并补发 cancel 帧（对齐 DSH signal aborted → cancelled）。
    /// 必须在 <c>turn/end</c> 之前调用，waterfall 生命周期才被开放轮次包住。
    /// </summary>
    /// <param name="sessionId">轮次所属会话</param>
    internal async Task CloseTurnAsync(string sessionId) {
        _agentId = null;
        var orphans = new List<Pending>();
        foreach (var (eventId, pending) in _pending) {
            if (string.Equals(pending.SessionId, sessionId, StringComparison.Ordinal) && _pending.TryRemove(eventId, out var removed)) {
                orphans.Add(removed);
            }
        }
        foreach (var orphan in orphans) {
            await CancelPendingAsync(orphan);
        }
    }

    /// <summary>
    /// 引擎提问入口：构造 <c>user-questions/request</c> 帧发给前端并等待回帖。
    /// 前端未连接 → 立即返回失败（错误信息给出可操作引导）；取消令牌触发 → 取消并补发 cancel 帧。
    /// </summary>
    /// <param name="questions">引擎问题列表（1-4 个）</param>
    /// <param name="cancellationToken">轮次取消令牌</param>
    /// <returns>答案批次映射后的引擎结果</returns>
    internal async Task<AskUserQuestionResult> RequestAsync(IReadOnlyList<QuestionItem> questions, CancellationToken cancellationToken) {
        var send = CurrentSend();
        if (send is null) {
            GuiDiag.Log("[DSH-QUESTION] no events stream: question rejected");
            return AskUserQuestionResult.FailureResult("前端未连接，无法展示提问卡片；请在 GUI 中打开会话后重试");
        }
        if (questions.Count == 0) {
            return AskUserQuestionResult.FailureResult("提问内容为空，未发送 user-questions/request");
        }

        var sessionId = _agentId;
        var eventId = Guid.NewGuid().ToString("N");
        var completion = new TaskCompletionSource<AskUserQuestionResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new Pending(eventId, sessionId, questions, completion);

        var frame = new DshWaterfallFrame {
            Event = "user-questions/request",
            EventId = eventId,
            AgentId = sessionId ?? "host",
            Request = JsonSerializer.SerializeToElement(
                new DshUserQuestionRequest {
                    Questions = [.. questions.Select((question, index) => new DshUserQuestion {
                        Id = $"q{index}",
                        Question = question.Question,
                        Header = question.Header,
                        Options = [.. question.Options.Select(option => new DshUserQuestionOption {
                            Label = option.Label,
                            Description = option.Description,
                        })],
                        MultiSelect = question.MultiSelect,
                    })],
                }, DshJsonContext.Default.DshUserQuestionRequest),
        };

        _pending[eventId] = pending;
        using var registration = cancellationToken.Register(() => {
            if (_pending.TryRemove(eventId, out var abandoned)) {
                _ = CancelPendingAsync(abandoned);
            }
        });
        try {
            await send(JsonSerializer.SerializeToElement(frame, DshJsonContext.Default.DshWaterfallFrame));
            GuiDiag.Log($"[DSH-QUESTION] waterfall sent event=user-questions/request id={eventId} count={questions.Count}");
        } catch (Exception ex) {
            _pending.TryRemove(eventId, out _);
            GuiDiag.Log($"[DSH-QUESTION] waterfall send failed: {ex.Message}");
            return AskUserQuestionResult.FailureResult($"提问卡片发送失败：{ex.Message}");
        }

        return await completion.Task;
    }

    /// <summary>
    /// 前端回帖结算：kind=result → 答案批次映射为 QuestionsResult；next/rejected/缺失 → 取消。
    /// clientId 与当前连接不符或 eventId 未知返回 false（幂等，不抛错）。
    /// </summary>
    /// <param name="clientId">回帖声称的客户端标识</param>
    /// <param name="eventId">waterfall 帧的 eventId</param>
    /// <param name="outcome">监听器处理结果</param>
    /// <returns>是否结算了一个挂起中的提问</returns>
    internal async Task<bool> SettleAsync(string clientId, string eventId, DshEventOutcome? outcome) {
        if (!IsCurrentClient(clientId)) {
            return false;
        }
        if (!_pending.TryRemove(eventId, out var pending)) {
            return false;
        }

        await SendCancelAsync(eventId);
        pending.Completion.TrySetResult(MapOutcome(pending, outcome));
        return true;
    }

    /// <summary>取消一次挂起提问：先发 cancel 帧停掉前端等待，再唤醒引擎</summary>
    /// <param name="pending">挂起中的提问</param>
    private async Task CancelPendingAsync(Pending pending) {
        await SendCancelAsync(pending.EventId);
        pending.Completion.TrySetResult(AskUserQuestionResult.CancelledResult());
    }

    /// <summary>发送 cancel 帧（通道已断时静默跳过；发送异常不影响引擎收尾）</summary>
    /// <param name="eventId">已结算的 waterfall 关联 ID</param>
    private async Task SendCancelAsync(string eventId) {
        if (CurrentSend() is not { } send) {
            return;
        }
        try {
            await send(JsonSerializer.SerializeToElement(
                new DshWaterfallCancelFrame { EventId = eventId },
                DshJsonContext.Default.DshWaterfallCancelFrame));
        } catch (Exception ex) {
            GuiDiag.Log($"[DSH-QUESTION] cancel frame send failed id={eventId}: {ex.Message}");
        }
    }

    /// <summary>回帖载荷 → 引擎结果（只有 kind=result 携带答案批次才是成功）</summary>
    /// <param name="pending">挂起中的提问（提供问题顺序与文本）</param>
    /// <param name="outcome">前端监听器结果</param>
    /// <returns>答案或取消</returns>
    private static AskUserQuestionResult MapOutcome(Pending pending, DshEventOutcome? outcome) {
        if (outcome is not { Kind: "result" } || outcome.Value is not { } value) {
            return AskUserQuestionResult.CancelledResult();
        }
        DshUserQuestionAnswerBatch? batch;
        try {
            batch = value.Deserialize(DshJsonContext.Default.DshUserQuestionAnswerBatch);
        } catch (JsonException ex) {
            GuiDiag.Log($"[DSH-QUESTION] answer payload invalid: {ex.Message}");
            return AskUserQuestionResult.CancelledResult();
        }
        if (batch is null) {
            return AskUserQuestionResult.CancelledResult();
        }

        var byId = batch.Answers
            .GroupBy(answer => answer.Id, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        var answers = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index < pending.Questions.Count; index++) {
            var question = pending.Questions[index];
            answers[question.Question] = byId.TryGetValue($"q{index}", out var answer)
                ? FormatAnswer(answer)
                : "";
        }
        return AskUserQuestionResult.QuestionsResult(answers);
    }

    /// <summary>选项标签按选择顺序逗号连接，自填文本非空时追加在末尾</summary>
    /// <param name="answer">单个问题的答案项</param>
    /// <returns>展示值（跳过时为空串）</returns>
    private static string FormatAnswer(DshUserQuestionAnswerItem answer) {
        var parts = new List<string>(answer.Selected.Count + 1);
        parts.AddRange(answer.Selected.Where(label => !string.IsNullOrWhiteSpace(label)));
        if (!string.IsNullOrWhiteSpace(answer.Custom)) {
            parts.Add(answer.Custom.Trim());
        }
        return string.Join(", ", parts);
    }

    /// <summary>同步读取当前发送通道（lock 收敛在同步方法内，规避 async 方法持锁）</summary>
    private Func<JsonElement, Task>? CurrentSend() {
        lock (_gate) { return _events?.Send; }
    }

    /// <summary>同步校验回帖客户端是否仍是当前 $events 连接（重连后的旧代际回帖幂等忽略）</summary>
    /// <param name="clientId">回帖声称的客户端标识</param>
    /// <returns>是否为当前连接</returns>
    private bool IsCurrentClient(string clientId) {
        lock (_gate) { return _events is { } channel && string.Equals(channel.ClientId, clientId, StringComparison.Ordinal); }
    }
}
