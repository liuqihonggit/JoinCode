namespace JoinCode.Gui.WebHost;

/// <summary>
/// 一轮对话的执行器（ADR 0143 M2-c2）：校验 <c>session/prompt</c> 请求 → 立即落用户消息事件 →
/// 后台跑自有引擎并把结果落成助手消息事件。事件经 <see cref="DshSessionStreamHub"/> 推给已打开的
/// <c>session/follow</c> 连接；同一会话同时只允许一轮。
/// </summary>
internal sealed class DshTurnRunner {
    private const int Step = 1;

    private readonly DshHostServices _services;

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
            return ("session/busy", $"会话 {request.SessionId} 上一轮仍在生成；等它结束（接口完成/停止）后再发送，排队与打断在 M2-c3 接入");
        }
        await _services.Streams.PublishAsync(request.SessionId, "turn/start", new DshTurnStartData { Turn = turn.Turn });
        await _services.Streams.PublishAsync(request.SessionId, "step/start", new DshStepData { Turn = turn.Turn, Step = Step });
        await _services.Streams.PublishAsync(request.SessionId, "user/message", new DshWireMessage {
            Id = $"{request.SessionId}:{turn.Turn}:user",
            Role = "user",
            Content = [new DshWireContentBlock { Text = text }],
            Source = new DshWireMessageSource { Kind = "user", RpcId = request.RequestId },
        }, surfaceOp: "append");
        _ = RunTurnAsync(engine, request.SessionId, text, turn.Turn, turn.Token);
        return null;
    }

    /// <summary>后台跑一轮生成：成功落助手消息，停止/失败各自落对应收尾（异常只记日志，不让进程崩）</summary>
    private async Task RunTurnAsync(Hosting.IJccChatSession engine, string sessionId, string text, int turn, CancellationToken cancellationToken) {
        try {
            engine.SwitchSession(sessionId);
            if (await _services.Sessions.HistoryAsync(sessionId, cancellationToken) is { Count: > 0 } history) {
                await engine.LoadHistoryAsync(history, cancellationToken);
            }
            App.LogDiag($"[DSH-WEB] turn streaming session={sessionId} turn={turn}");
            var content = new StringBuilder();
            var events = 0;
            await foreach (var evt in engine.StreamAsync(text, cancellationToken)) {
                events += 1;
                if (events == 1) {
                    App.LogDiag($"[DSH-WEB] turn first event type={evt.Type} session={sessionId}");
                }
                if (evt.Type == ChatStreamEventType.Content && !string.IsNullOrEmpty(evt.Content)) {
                    content.Append(evt.Content);
                }
            }
            App.LogDiag($"[DSH-WEB] turn stream done session={sessionId} events={events} chars={content.Length}");
            await PublishReplyAsync(sessionId, turn, content.ToString(), engine);
            await EndTurnAsync(sessionId, turn, new DshTurnEndReason { Kind = "completed" });
        } catch (OperationCanceledException) {
            App.LogDiag($"[DSH-WEB] turn cancelled session={sessionId} turn={turn}");
            await EndTurnAsync(sessionId, turn, new DshTurnEndReason {
                Kind = "aborted",
                Reason = new DshTurnEndCancelCause { Kind = "user" },
            });
        } catch (Exception ex) {
            App.LogDiag($"[DSH-WEB] turn failed session={sessionId} turn={turn}: {ex.Message}");
            await EndTurnAsync(sessionId, turn, new DshTurnEndReason {
                Kind = "error",
                Error = new DshTurnEndError { Code = "UNKNOWN", Message = ex.Message },
            });
        } finally {
            _services.Streams.EndTurn(sessionId);
        }
    }

    /// <summary>落助手回复事件（空回复不落，避免空气泡）</summary>
    private async Task PublishReplyAsync(string sessionId, int turn, string reply, Hosting.IJccChatSession engine) {
        if (!string.IsNullOrWhiteSpace(reply)) {
            await _services.Streams.PublishAsync(sessionId, "assistant/message", new DshAssistantMessageData {
                Turn = turn,
                Step = Step,
                Message = new DshWireMessage {
                    Id = $"{sessionId}:{turn}:assistant",
                    Role = "assistant",
                    Content = [new DshWireContentBlock { Text = reply }],
                    Source = new DshWireMessageSource {
                        Kind = "model",
                        Provider = engine.CurrentVendor,
                        Model = engine.CurrentModelId,
                    },
                },
            }, surfaceOp: "append");
        }
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
}
