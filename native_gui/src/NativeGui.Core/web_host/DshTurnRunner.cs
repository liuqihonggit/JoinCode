namespace JoinCode.Gui.WebHost;

/// <summary>
/// 一轮对话的执行器（ADR 0143 M2-c2）：校验 <c>session/prompt</c> 请求 → 立即落用户消息事件 →
/// 后台跑自有引擎并把结果落成助手消息事件。事件经 <see cref="DshSessionStreamHub"/> 推给已打开的
/// <c>session/follow</c> 连接；同一会话同时只允许一轮。
/// </summary>
internal sealed class DshTurnRunner {
    private const int Step = 1;

    private readonly DshHostServices _services;

    /// <summary>会话 → 待跑队列（mode=queue：上一轮进行中时把新输入排队，轮次结束后按序执行）</summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, System.Collections.Concurrent.ConcurrentQueue<PendingPrompt>> _pending = new(StringComparer.Ordinal);

    /// <summary>构造执行器</summary>
    /// <param name="services">宿主服务容器</param>
    internal DshTurnRunner(DshHostServices services) => _services = services;

    /// <summary>
    /// 受理一条用户输入：成功后立即返回（本轮在后台跑），失败返回可执行的引导错误。
    /// </summary>
    /// <param name="request">prompt 参数</param>
    /// <param name="cancellationToken">受理阶段的取消令牌</param>
    /// <returns>失败时的错误码与引导文案；受理成功返回 null</returns>
    internal async Task<(string Code, string Message)?> StartAsync(DshSessionPromptArgs request, CancellationToken cancellationToken) {
        if (_services.Engine is not { } engine) {
            return ("session/not-ready", "引擎仍在组装中，稍后重试；若长时间未就绪查看 dumps/startup_timing.log");
        }
        if (request.Content.Any(part => part.Type != "text")) {
            return ("session/rejected", "session/prompt 目前只支持文本输入；图片与文件请先在输入框里移除，等 M2-c3 接入附件后重试");
        }
        var text = string.Concat(request.Content.Select(part => part.Text));
        if (string.IsNullOrWhiteSpace(text)) {
            return ("session/rejected", "session/prompt 需要非空文本内容；在输入框里输入文字后重试");
        }
        if (await _services.Sessions.SnapshotAsync(request.SessionId, cancellationToken) is not { } snapshot) {
            return ("session/unknown", $"会话 {request.SessionId} 不在磁盘上；先用 session/create 建会话，或从侧栏选一个已有会话");
        }
        _services.Streams.Observe(request.SessionId, snapshot.Cursor, MaxTurn(snapshot.Records));
        if (_services.Streams.TryBeginTurn(request.SessionId) is not { } turn) {
            // 轮内注入（B11）：轮次运行中不排队、不开新轮，直接投递到引擎邮箱（DSH agent.steer）
            if (string.Equals(request.Mode, "steer", StringComparison.Ordinal)) {
                return TrySteer(engine, request.SessionId, text);
            }
            if (string.Equals(request.Mode, "queue", StringComparison.Ordinal)) {
                Queue(request.SessionId, NewPending(request.RequestId, text));
                return null;
            }
            return ("session/busy", $"会话 {request.SessionId} 上一轮仍在生成；等它结束（接口完成/停止）后再发送。选项：①轮内注入请在请求里带 mode=steer；②排队等下一轮请用 mode=queue；③停止当前轮用 session/cancel");
        }
        await BeginTurnAsync(engine, request.SessionId, NewPending(request.RequestId, text), turn);
        return null;
    }

    /// <summary>
    /// 投递轮内注入（轮次运行中）。引擎未装配邮箱时诚实拒绝并引导（不静默丢弃、不改排队语义）。
    /// </summary>
    /// <param name="engine">引擎会话</param>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="text">已校验文本</param>
    /// <returns>失败时的错误码与引导文案；受理成功返回 null</returns>
    private static (string Code, string Message)? TrySteer(Hosting.IJccChatSession engine, string sessionId, string text) =>
        engine.TrySteerInput(sessionId, text)
            ? null
            : ("session/steer-unavailable",
                $"会话 {sessionId} 的引擎未装配轮内注入邮箱（ISteerInputMailbox）；请重启宿主。当前输入未被丢弃：改用 mode=queue 会在本轮结束后顺序执行");

    /// <summary>落用户消息并启动后台轮次（开启/排队复用）</summary>
    /// <param name="engine">引擎会话</param>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="prompt">已校验的输入</param>
    /// <param name="turn">轮次占用</param>
    private async Task BeginTurnAsync(Hosting.IJccChatSession engine, string sessionId, PendingPrompt prompt, (int Turn, CancellationToken Token) turn) {
        await _services.Streams.PublishAsync(sessionId, "turn/start", new DshTurnStartData { Turn = turn.Turn });
        await _services.Streams.PublishAsync(sessionId, "step/start", new DshStepData { Turn = turn.Turn, Step = Step });
        await _services.Streams.PublishAsync(sessionId, "user/message", new DshWireMessage {
            Id = $"{sessionId}:{turn.Turn}:user",
            Role = "user",
            Content = [new DshWireContentBlock { Text = prompt.Text }],
            Source = new DshWireMessageSource { Kind = "user", RpcId = prompt.RequestId },
        }, surfaceOp: "append");
        _ = RunTurnAsync(engine, sessionId, prompt.Text, turn.Turn, turn.Token);
    }

    /// <summary>把输入放进会话队列（ItemId = GUID，前端 updateQueue 的行操作句柄）</summary>
    private void Queue(string sessionId, PendingPrompt prompt) =>
        _pending.GetOrAdd(sessionId, _ => new System.Collections.Concurrent.ConcurrentQueue<PendingPrompt>()).Enqueue(prompt);

    /// <summary>构造带操作句柄的排队项</summary>
    private static PendingPrompt NewPending(string requestId, string text) =>
        new(Guid.NewGuid().ToString("N"), requestId, text);

    /// <summary>轮次收尾后启动下一条排队输入（无排队则返回）</summary>
    private void StartQueued(Hosting.IJccChatSession engine, string sessionId) {
        if (!_pending.TryGetValue(sessionId, out var queue) || !queue.TryDequeue(out var prompt)) {
            return;
        }
        if (_services.Streams.TryBeginTurn(sessionId) is not { } turn) {
            Queue(sessionId, prompt);
            return;
        }
        _ = BeginTurnAsync(engine, sessionId, prompt, turn);
    }

    /// <summary>后台跑一轮生成：成功落助手消息，停止/失败各自落对应收尾（异常只记日志，不让进程崩）</summary>
    private async Task RunTurnAsync(Hosting.IJccChatSession engine, string sessionId, string text, int turn, CancellationToken cancellationToken) {
        // 权限确认 waterfall 的目标会话（前端按 agentId 路由到对应会话 scope 的审批面板，TASK058 B1）
        _services.Approvals.SetAgent(sessionId);
        // 子代理轮内运行追踪（tool-workflow 事件族，TASK058 B6）— try 外声明，取消/异常分支也要收尾
        var agents = new DshAgentRunTracker($"{sessionId}:{turn}:agents");
        try {
            engine.SwitchSession(sessionId);
            if (await _services.Sessions.HistoryAsync(sessionId, cancellationToken) is { Count: > 0 } history) {
                await engine.LoadHistoryAsync(history, cancellationToken);
            }
            GuiDiag.Log($"[DSH-WEB] turn streaming session={sessionId} turn={turn}");
            var content = new StringBuilder();
            var thinking = new StringBuilder();
            TokenUsage? usage = null;
            var events = 0;
            var steers = 0;
            var calls = new List<(string CallId, string Name, string Arguments)>();
            await foreach (var evt in engine.StreamAsync(text, cancellationToken)) {
                events += 1;
                if (events == 1) {
                    GuiDiag.Log($"[DSH-WEB] turn first event type={evt.Type} session={sessionId}");
                }
                if (evt.Type == ChatStreamEventType.Content && !string.IsNullOrEmpty(evt.Content)) {
                    content.Append(evt.Content);
                } else if (evt.Type == ChatStreamEventType.Thinking && !string.IsNullOrEmpty(evt.ThinkingContent)) {
                    // 思考内容 → assistant/message 前置 reasoning 块（TASK058 B4，前端折叠渲染）
                    thinking.Append(evt.ThinkingContent);
                } else if (evt.Type == ChatStreamEventType.Complete && evt.Usage is { } turnUsage) {
                    // 用量 → data.usage 四桶（前端"本轮用量"弹窗，TASK058 B4）
                    usage = turnUsage;
                } else if (evt.Type == ChatStreamEventType.AgentStarted || evt.Type == ChatStreamEventType.AgentFinished) {
                    await PublishAgentEventsAsync(sessionId, agents, evt);
                } else if (evt.Type == ChatStreamEventType.SystemNotice) {
                    GuiDiag.Log($"[DSH-WEB] turn system notice session={sessionId}: {evt.Content}");
                } else if (evt.Type == ChatStreamEventType.ToolCallEnd || evt.Type == ChatStreamEventType.ToolCallStart) {
                    await PublishToolCallAsync(sessionId, turn, evt, calls);
                } else if (evt.Type == ChatStreamEventType.SteerInput && !string.IsNullOrEmpty(evt.Content)) {
                    // 轮内注入在步首已进入上下文 → 这里补投影为轮内 user/message（DSH :1061 语义）
                    steers += 1;
                    await PublishSteerMessageAsync(sessionId, turn, evt.Content, steers);
                }
            }
            GuiDiag.Log($"[DSH-WEB] turn stream done session={sessionId} events={events} chars={content.Length} thinking={thinking.Length}");
            await PublishReplyAsync(sessionId, turn, thinking.Length > 0 ? thinking.ToString() : null, content.ToString(), usage, engine);
            await PublishWorkflowRunEndAsync(sessionId, agents, "completed");
            await FinishTurnAsync(sessionId, turn, new DshTurnEndReason { Kind = "completed" });
        } catch (OperationCanceledException) {
            GuiDiag.Log($"[DSH-WEB] turn cancelled session={sessionId} turn={turn}");
            await PublishWorkflowRunEndAsync(sessionId, agents, "cancelled");
            await FinishTurnAsync(sessionId, turn, new DshTurnEndReason {
                Kind = "aborted",
                Reason = new DshTurnEndCancelCause { Kind = "user" },
            });
        } catch (Exception ex) {
            GuiDiag.Log($"[DSH-WEB] turn failed session={sessionId} turn={turn}: {ex.Message}");
            await PublishWorkflowRunEndAsync(sessionId, agents, "error");
            await FinishTurnAsync(sessionId, turn, new DshTurnEndReason {
                Kind = "error",
                Error = new DshTurnEndError { Code = "UNKNOWN", Message = ex.Message },
            });
        } finally {
            _services.Streams.EndTurn(sessionId);
            if (_services.Engine is { } ready) {
                StartQueued(ready, sessionId);
            }
        }
    }

    /// <summary>
    /// 收尾一轮：**先**结清审批审计对（挂起确认结算 + 断流补写排空），**再**落 <c>turn/end</c>。
    /// 顺序不可颠倒：DSH 要求 <c>approval/asked</c>/<c>approval/decided</c> 必须被开放轮次包住
    /// （<c>invariant.js</c> 判定 outside an open turn 即错），取消/失败路径同样适用。
    /// </summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="turn">轮次号</param>
    /// <param name="reason">轮次结束原因</param>
    private async Task FinishTurnAsync(string sessionId, int turn, DshTurnEndReason reason) {
        await _services.Approvals.CloseTurnAsync(sessionId);
        await EndTurnAsync(sessionId, turn, reason);
    }

    /// <summary>发布子代理事件族（agent start/end → tool-workflow，TASK058 B6）</summary>
    private async Task PublishAgentEventsAsync(string sessionId, DshAgentRunTracker agents, ChatStreamEvent evt) {
        var events = evt.Type == ChatStreamEventType.AgentStarted
            ? agents.OnAgentStarted(evt)
            : agents.OnAgentFinished(evt);
        foreach (var workflowEvent in events) {
            await PublishWorkflowEventAsync(sessionId, workflowEvent);
        }
    }

    /// <summary>发布 run-end 收尾（无成员时 tracker 返回 null，跳过）</summary>
    private async Task PublishWorkflowRunEndAsync(string sessionId, DshAgentRunTracker agents, string stopReason) {
        if (agents.BuildRunEnd(stopReason) is { } runEnd) {
            await PublishWorkflowEventAsync(sessionId, runEnd);
        }
    }

    /// <summary>落一条 workflow 事件（data 已是树，直接进事件流）</summary>
    private async Task PublishWorkflowEventAsync(string sessionId, DshAgentRunTracker.WorkflowEvent workflowEvent) {
        await _services.Streams.PublishAsync(sessionId, workflowEvent.Type, workflowEvent.Data);
    }

    /// <summary>落一次工具调用或工具结果事件（callId 缺失时按序号补齐，保证调用与结果可配对）</summary>
    private async Task PublishToolCallAsync(string sessionId, int turn, ChatStreamEvent evt, List<(string CallId, string Name, string Arguments)> calls) {
        var callId = string.IsNullOrEmpty(evt.ToolCallId) ? $"{sessionId}:{turn}:tool:{calls.Count + 1}" : evt.ToolCallId;
        var name = evt.ToolName ?? "tool";
        if (evt.Type == ChatStreamEventType.ToolCallStart) {
            var arguments = evt.ToolArguments ?? evt.Content ?? "";
            calls.Add((callId, name, arguments));
            await _services.Streams.PublishAsync(sessionId, "tool/call", new DshToolCallData {
                Turn = turn,
                Step = Step,
                CallId = callId,
                Name = name,
                Arguments = arguments,
            });
            return;
        }
        var text = evt.ToolResultText ?? evt.Content ?? "";
        await _services.Streams.PublishAsync(sessionId, "tool/result", new DshToolResultData {
            Turn = turn,
            Step = Step,
            Message = new DshWireMessage {
                Id = $"{callId}:result",
                Role = "tool",
                ToolCallId = callId,
                IsError = evt.IsToolError,
                Content = [new DshWireContentBlock { Text = text }],
                Source = new DshWireMessageSource { Kind = "tool", CallId = callId },
            },
        }, surfaceOp: "append");
    }

    /// <summary>落助手回复事件（空块序列不落，避免空气泡；思考/正文/用量经投影单点翻译，TASK058 B4）</summary>
    private async Task PublishReplyAsync(string sessionId, int turn, string? thinking, string reply, TokenUsage? usage, Hosting.IJccChatSession engine) {
        var (content, mappedUsage) = DshAssistantReplyProjection.Project(thinking, reply, usage);
        if (content.Count == 0) {
            return;
        }
        await _services.Streams.PublishAsync(sessionId, "assistant/message", new DshAssistantMessageData {
            Turn = turn,
            Step = Step,
            Message = new DshWireMessage {
                Id = $"{sessionId}:{turn}:assistant",
                Role = "assistant",
                Content = content,
                Source = new DshWireMessageSource {
                    Kind = "model",
                    Provider = engine.CurrentVendor,
                    Model = engine.CurrentModelId,
                },
            },
            Usage = mappedUsage,
        }, surfaceOp: "append");
    }

    /// <summary>
    /// 落一条轮内注入的用户消息（轮内 <c>user/message</c>，<c>surfaceOp=append</c>）：
    /// 与开轮时的首条用户消息同形，前端据此在对话流中插入新的用户气泡（不新开轮次）。
    /// </summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="turn">轮次号</param>
    /// <param name="text">注入文本</param>
    /// <param name="index">本轮第几条注入（保证消息 ID 唯一）</param>
    private async Task PublishSteerMessageAsync(string sessionId, int turn, string text, int index) {
        await _services.Streams.PublishAsync(sessionId, "user/message", new DshWireMessage {
            Id = $"{sessionId}:{turn}:steer:{index}",
            Role = "user",
            Content = [new DshWireContentBlock { Text = text }],
            Source = new DshWireMessageSource { Kind = "user" },
        }, surfaceOp: "append");
    }

    /// <summary>落步骤结束与轮次结束（先 step/end 再 turn/end）</summary>
    private async Task EndTurnAsync(string sessionId, int turn, DshTurnEndReason reason) {
        await _services.Streams.PublishAsync(sessionId, "step/end", new DshStepData { Turn = turn, Step = Step });
        await _services.Streams.PublishAsync(sessionId, "turn/end", new DshTurnEndData { Turn = turn, Reason = reason });
    }

    /// <summary>快照事件页里的最大轮次（无事件为 0），用于让新轮次接着历史编号</summary>
    private static int MaxTurn(IReadOnlyList<DshSessionRecord> records) =>
        records
            .Where(record => record.Event.Type == "turn/start")
            .Select(record => record.Event.Data.TryGetProperty("turn", out var turn) && turn.TryGetInt32(out var value) ? value : 0)
            .DefaultIfEmpty(0)
            .Max();

    /// <summary>排队中的一条用户输入</summary>
    /// <param name="RequestId">提交身份（回填 user/message 的 source.rpcId）</param>
    /// <param name="Text">已校验文本</param>
    private sealed record PendingPrompt(string ItemId, string RequestId, string Text);

    /// <summary>队列项快照（ListQueue 供 updateQueue 与测试消费）</summary>
    internal sealed record QueueItem(string ItemId, string RequestId, string Text);

    /// <summary>会话队列快照（按入队顺序）</summary>
    internal IReadOnlyList<QueueItem> ListQueue(string sessionId) {
        if (!_pending.TryGetValue(sessionId, out var queue)) {
            return [];
        }
        var snapshot = new List<QueueItem>();
        foreach (var prompt in queue) {
            snapshot.Add(new QueueItem(prompt.ItemId, prompt.RequestId, prompt.Text));
        }
        return snapshot;
    }

    /// <summary>编辑排队项文本（无该项返回 false）</summary>
    internal bool TryEditQueueItem(string sessionId, string itemId, string text) {
        if (!_pending.TryGetValue(sessionId, out var queue)) {
            return false;
        }
        lock (queue) {
            var rewritten = false;
            var rebuilt = new System.Collections.Concurrent.ConcurrentQueue<PendingPrompt>();
            foreach (var prompt in queue) {
                if (string.Equals(prompt.ItemId, itemId, StringComparison.Ordinal)) {
                    rebuilt.Enqueue(prompt with { Text = text });
                    rewritten = true;
                } else {
                    rebuilt.Enqueue(prompt);
                }
            }
            if (!rewritten) {
                return false;
            }
            _pending[sessionId] = rebuilt;
            return true;
        }
    }

    /// <summary>移除排队项（无该项返回 false）</summary>
    internal bool TryRemoveQueueItem(string sessionId, string itemId) {
        if (!_pending.TryGetValue(sessionId, out var queue)) {
            return false;
        }
        lock (queue) {
            var removed = false;
            var rebuilt = new System.Collections.Concurrent.ConcurrentQueue<PendingPrompt>();
            foreach (var prompt in queue) {
                if (string.Equals(prompt.ItemId, itemId, StringComparison.Ordinal)) {
                    removed = true;
                } else {
                    rebuilt.Enqueue(prompt);
                }
            }
            if (!removed) {
                return false;
            }
            _pending[sessionId] = rebuilt;
            return true;
        }
    }
}
