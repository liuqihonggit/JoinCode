namespace JoinCode.Gui.WebHost;

/// <summary>
/// 会话事件流中枢（ADR 0143 M2-c2）：登记打开的 <c>session/follow</c> 连接、按会话分配事件序号，
/// 并把一轮对话产生的事件推给该会话的所有订阅者。序号从打开快照的页尾光标续起，
/// 保证「历史投影 + 实时事件」拼成一条单调序列。
/// </summary>
internal sealed class DshSessionStreamHub {
    private readonly Lock _gate = new();
    private readonly Dictionary<string, SessionState> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Subscription> _subscriptions = new(StringComparer.Ordinal);

    /// <summary>登记一条打开的流（返回的句柄在流关闭时注销）</summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="streamId">前端流身份</param>
    /// <param name="send">推送一条事件的回调（由 WebSocket 层串行发送）</param>
    internal IDisposable Subscribe(string sessionId, string streamId, Func<DshSessionEvent, Task> send) {
        var subscription = new Subscription(sessionId, streamId, send);
        lock (_gate) {
            _subscriptions[streamId] = subscription;
        }
        return new Unsubscriber(this, streamId);
    }

    /// <summary>对齐快照状态：页尾光标与轮次取已知最大值（重连时不会倒退回旧值）</summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="cursor">快照页尾光标（无事件为 -1）</param>
    /// <param name="turn">快照里的最大轮次（无事件为 0）</param>
    internal void Observe(string sessionId, long cursor, int turn) {
        lock (_gate) {
            var state = State(sessionId);
            state.Cursor = Math.Max(state.Cursor, cursor);
            state.Turn = Math.Max(state.Turn, turn);
        }
    }

    /// <summary>尝试占用一轮生成（同一会话同时只允许一轮）</summary>
    /// <param name="sessionId">会话 ID</param>
    /// <returns>占用成功返回下一轮编号，否则 null（已有轮次在跑）</returns>
    internal int? TryBeginTurn(string sessionId) {
        lock (_gate) {
            var state = State(sessionId);
            if (state.Running) {
                return null;
            }
            state.Running = true;
            state.Turn += 1;
            return state.Turn;
        }
    }

    /// <summary>结束一轮生成（释放占用，允许下一轮）</summary>
    /// <param name="sessionId">会话 ID</param>
    internal void EndTurn(string sessionId) {
        lock (_gate) {
            State(sessionId).Running = false;
        }
    }

    /// <summary>当前进行中的轮次编号（空闲为 0）</summary>
    /// <param name="sessionId">会话 ID</param>
    internal int RunningTurn(string sessionId) {
        lock (_gate) {
            var state = State(sessionId);
            return state.Running ? state.Turn : 0;
        }
    }

    /// <summary>分配序号并推给该会话的全部订阅者（无订阅者时仍然推进序号，重连后快照会重算）</summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="type">事件类型</param>
    /// <param name="data">事件载荷（已注册到 <see cref="DshJsonContext"/>）</param>
    /// <param name="surfaceOp">会话表面操作（消息类事件用 append）</param>
    internal async Task PublishAsync<T>(string sessionId, string type, T data, string? surfaceOp = null) {
        var (sessionEvent, targets) = Allocate(sessionId, type, data, surfaceOp);
        foreach (var target in targets) {
            try {
                await target.Send(sessionEvent);
            } catch (Exception ex) {
                App.LogDiag($"[DSH-WEB] stream push failed stream={target.StreamId}: {ex.Message}");
            }
        }
    }

    /// <summary>在锁内分配序号并快照当前订阅者（await 一律在锁外）</summary>
    private (DshSessionEvent Event, Subscription[] Targets) Allocate<T>(string sessionId, string type, T data, string? surfaceOp) {
        lock (_gate) {
            var state = State(sessionId);
            var sessionEvent = new DshSessionEvent {
                Type = type,
                Seq = state.NextSeq(),
                Time = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Data = JsonSerializer.SerializeToElement(data, DshJsonContext.Default.GetTypeInfo(typeof(T))),
                SurfaceOp = surfaceOp,
            };
            return (sessionEvent, [.. _subscriptions.Values.Where(subscription => subscription.SessionId == sessionId)]);
        }
    }

    private SessionState State(string sessionId) {
        if (!_sessions.TryGetValue(sessionId, out var state)) {
            state = new SessionState();
            _sessions[sessionId] = state;
        }
        return state;
    }

    private void Unsubscribe(string streamId) {
        lock (_gate) {
            _subscriptions.Remove(streamId);
        }
    }

    /// <summary>一条打开的事件流</summary>
    private sealed record Subscription(string SessionId, string StreamId, Func<DshSessionEvent, Task> Send);

    /// <summary>会话的事件游标与生成状态</summary>
    private sealed class SessionState {
        /// <summary>最后分配的序号（初始 -1 = 空日志）</summary>
        internal long Cursor { get; set; } = -1;
        /// <summary>最近一次轮次编号</summary>
        internal int Turn { get; set; }
        /// <summary>是否有轮次在进行</summary>
        internal bool Running { get; set; }

        /// <summary>取下一个序号（并推进游标）</summary>
        internal long NextSeq() => ++Cursor;
    }

    /// <summary>订阅句柄</summary>
    private sealed class Unsubscriber(DshSessionStreamHub hub, string streamId) : IDisposable {
        private int _disposed;

        /// <summary>注销订阅（幂等）</summary>
        public void Dispose() {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) {
                hub.Unsubscribe(streamId);
            }
        }
    }
}
