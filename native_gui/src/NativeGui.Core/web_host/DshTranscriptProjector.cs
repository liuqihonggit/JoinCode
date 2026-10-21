namespace JoinCode.Gui.WebHost;

/// <summary>
/// 引擎转录 → DSH 会话事件投影（ADR 0143 M2-c）：把落盘会话的 user/assistant 消息还原成 DSH 的
/// <c>turn/start → step/start → user/message → assistant/message → step/end → turn/end</c> 事件序列，
/// 供 <c>session/follow</c> 打开快照把历史渲染成聊天记录（消息类事件带 <c>surfaceOp='append'</c>）。
/// </summary>
internal static class DshTranscriptProjector {
    /// <summary>投影会话事件页（seq 从 0 起单调递增；元数据类条目与侧链消息不参与）</summary>
    internal static List<DshSessionRecord> Project(string sessionId, IReadOnlyList<TranscriptEntry> entries) {
        var records = new List<DshSessionRecord>();
        var state = new ProjectionState();
        foreach (var entry in entries.Where(entry => IsConversationMessage(entry))) {
            state.At(entry.Timestamp);
            if (entry.Role == "user") {
                state.CloseTurn(records);
                state.OpenTurn(records);
                state.EmitMessage(records, sessionId, entry, "user");
            } else {
                state.EnsureTurn(records);
                state.EmitAssistant(records, sessionId, entry);
            }
        }
        state.CloseTurn(records);
        return records;
    }

    /// <summary>普通对话消息（剔除重命名/代理名等元数据条目与侧链消息）</summary>
    private static bool IsConversationMessage(TranscriptEntry entry) =>
        entry.Type is null
        && !entry.IsSidechain
        && !string.IsNullOrWhiteSpace(entry.Content)
        && entry.Role is "user" or "assistant";

    /// <summary>投影过程中的轮次/步骤/序号/时间游标</summary>
    private sealed class ProjectionState {
        private const int Step = 1;
        private long _seq;
        private int _turn;
        private long _time;
        private bool _turnOpen;

        /// <summary>切换到当前处理条目的时间戳</summary>
        internal void At(DateTime timestamp) => _time = new DateTimeOffset(
            timestamp.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(timestamp, DateTimeKind.Utc) : timestamp).ToUnixTimeMilliseconds();

        /// <summary>开启新一轮（turn/start + step/start）</summary>
        internal void OpenTurn(List<DshSessionRecord> records) {
            _turn += 1;
            _turnOpen = true;
            Emit(records, "turn/start", new DshTurnStartData { Turn = _turn });
            Emit(records, "step/start", new DshStepData { Turn = _turn, Step = Step });
        }

        /// <summary>确保存在打开的轮次（会话以助手消息开头时补齐轮次脚手架）</summary>
        internal void EnsureTurn(List<DshSessionRecord> records) {
            if (!_turnOpen) OpenTurn(records);
        }

        /// <summary>关闭当前轮次（step/end + turn/end），未开启时无副作用</summary>
        internal void CloseTurn(List<DshSessionRecord> records) {
            if (!_turnOpen) return;
            Emit(records, "step/end", new DshStepData { Turn = _turn, Step = Step });
            Emit(records, "turn/end", new DshTurnEndData { Turn = _turn });
            _turnOpen = false;
        }

        /// <summary>落一条用户/助手消息事件（带会话表面 append 标记）</summary>
        internal void EmitMessage(List<DshSessionRecord> records, string sessionId, TranscriptEntry entry, string role) =>
            Emit(records, $"{role}/message", BuildMessage(sessionId, entry, role), surfaceOp: "append");

        /// <summary>落一条助手消息事件（turn/step 包装 + 空流式块）</summary>
        internal void EmitAssistant(List<DshSessionRecord> records, string sessionId, TranscriptEntry entry) =>
            Emit(records, "assistant/message", new DshAssistantMessageData {
                Turn = _turn,
                Step = Step,
                Message = BuildMessage(sessionId, entry, "assistant"),
            }, surfaceOp: "append");

        /// <summary>消息身份按「会话 + 序号」稳定生成，便于重放与断言</summary>
        private DshWireMessage BuildMessage(string sessionId, TranscriptEntry entry, string role) => new() {
            Id = $"{sessionId}:{_seq}",
            Role = role,
            Content = [new DshWireContentBlock { Text = entry.Content }],
            Source = role == "user"
                ? new DshWireMessageSource { Kind = "user", RpcId = $"engine-{_seq}" }
                : new DshWireMessageSource {
                    Kind = "model",
                    Provider = entry.AgentId ?? "engine",
                    Model = entry.ModelId ?? "engine",
                },
        };

        /// <summary>落一条事件记录（顺带推进序号）</summary>
        private void Emit<T>(List<DshSessionRecord> records, string type, T data, string? surfaceOp = null) {
            records.Add(new DshSessionRecord {
                Event = new DshSessionEvent {
                    Type = type,
                    Seq = _seq,
                    Time = _time,
                    Data = JsonSerializer.SerializeToElement(data, DshJsonContext.Default.GetTypeInfo(typeof(T))),
                    SurfaceOp = surfaceOp,
                },
            });
            _seq += 1;
        }
    }
}
