namespace JoinCode.Gui.WebHost;

/// <summary>
/// 会话事件流中枢（ADR 0143 M2-c2）：登记打开的 <c>session/follow</c> 连接、按会话分配事件序号，
/// 并把一轮对话产生的事件推给该会话的所有订阅者。序号与事件一起持久化到 <see cref="DshSessionJournal"/>，
/// 保证「历史投影 + 实时事件」拼成一条跨进程重启也单调连续的序列。
/// </summary>
internal sealed class DshSessionStreamHub {
    private readonly Lock _gate = new();
    private readonly Dictionary<string, SessionState> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Subscription> _subscriptions = new(StringComparer.Ordinal);
    private readonly DshSessionJournal? _journal;

    /// <summary>构造中枢</summary>
    /// <param name="journal">事件日志（null = 纯内存，仅测试用）</param>
    internal DshSessionStreamHub(DshSessionJournal? journal = null) => _journal = journal;

    /// <summary>确保该会话的事件日志已从磁盘载入（打开流 / 读快照 / 翻页前调用）</summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="cancellationToken">取消令牌</param>
    internal async Task EnsureLoadedAsync(string sessionId, CancellationToken cancellationToken = default) {
        if (_journal is null || IsLoadedCore(sessionId)) {
            return;
        }
        var events = await _journal.LoadAsync(sessionId, cancellationToken);
        ApplyLoaded(sessionId, events);
    }

    /// <summary>该会话是否已载入日志（未载入时 snapshot 会走转录投影）</summary>
    private bool IsLoadedCore(string sessionId) {
        lock (_gate) {
            return _sessions.TryGetValue(sessionId, out var state) && state.Loaded;
        }
    }

    /// <summary>把磁盘日志合并进会话历史（只在同步段持锁）</summary>
    private void ApplyLoaded(string sessionId, DshSessionEvent[] events) {
        lock (_gate) {
            var state = State(sessionId);
            state.Loaded = true;
            if (events.Length == 0) {
                return;
            }
            state.History.Clear();
            state.History.AddRange(events);
            state.Cursor = Math.Max(state.Cursor, events[^1].Seq);
            state.Turn = Math.Max(state.Turn, MaxTurn(events));
        }
    }

    /// <summary>登记一条打开的流（返回的句柄在流关闭时注销）</summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="streamId">前端流身份</param>
    /// <param name="send">推送一条事件的回调（由 WebSocket 层串行发送）</param>
    /// <param name="lastSeq">该流已经拿到的最后一个序号（快照光标；无快照为 -1）</param>
    internal IDisposable Subscribe(string sessionId, string streamId, Func<DshSessionEvent, Task> send, long lastSeq = -1) {
        var subscription = new Subscription(sessionId, streamId, send) { LastSeq = lastSeq };
        lock (_gate) {
            _subscriptions[streamId] = subscription;
        }
        return new Unsubscriber(this, streamId);
    }

    /// <summary>把订阅者尚未收到的历史事件补发一遍（打开流送出快照后调用，闭合「快照与订阅之间」的窗口）</summary>
    /// <param name="streamId">前端流身份</param>
    internal async Task FlushAsync(string streamId) {
        var (subscription, pending) = TakePending(streamId);
        if (subscription is not null) {
            await DeliverAsync(subscription, pending);
        }
    }

    /// <summary>取订阅者与其待发事件（同步持锁，供异步方法调用）</summary>
    private (Subscription? Subscription, DshSessionEvent[] Pending) TakePending(string streamId) {
        lock (_gate) {
            return _subscriptions.TryGetValue(streamId, out var subscription)
                ? (subscription, Pending(subscription))
                : (null, []);
        }
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

    /// <summary>取该会话已记录的全部事件（调用前须 <see cref="EnsureLoadedAsync"/>）</summary>
    /// <param name="sessionId">会话 ID</param>
    internal DshSessionEvent[] Events(string sessionId) {
        lock (_gate) {
            return _sessions.TryGetValue(sessionId, out var state) ? [.. state.History] : [];
        }
    }

    /// <summary>该会话的事件历史是否已从磁盘载入</summary>
    /// <param name="sessionId">会话 ID</param>
    internal bool IsLoaded(string sessionId) => IsLoadedCore(sessionId);

    /// <summary>
    /// 用转录投影播种事件日志（仅当日志为空时）：宿主此前没有记录过该会话，
    /// 但引擎转录里有历史 —— 先落盘，未来的序号从投影页尾继续，重启后仍能完整还原。
    /// </summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="records">转录投影出的历史记录</param>
    internal async Task SeedAsync(string sessionId, IReadOnlyList<DshSessionRecord> records) {
        if (records.Count == 0) {
            return;
        }
        if (_journal is not null && !WillSeed(sessionId)) {
            return;
        }
        if (_journal is not null) {
            foreach (var record in records) {
                await _journal.AppendAsync(sessionId, record.Event);
            }
        }
        ApplySeed(sessionId, records);
    }

    /// <summary>把播种事件写入内存状态（同步持锁，供异步方法调用）</summary>
    private void ApplySeed(string sessionId, IReadOnlyList<DshSessionRecord> records) {
        lock (_gate) {
            var state = State(sessionId);
            state.Loaded = true;
            if (state.History.Count > 0) {
                return;
            }
            state.History.AddRange(records.Select(record => record.Event));
            state.Cursor = Math.Max(state.Cursor, state.History[^1].Seq);
            state.Turn = Math.Max(state.Turn, MaxTurn([.. state.History]));
        }
    }

    /// <summary>该会话是否尚未播种（同步持锁，避免重复写入日志）</summary>
    private bool WillSeed(string sessionId) {
        lock (_gate) {
            return !_sessions.TryGetValue(sessionId, out var state) || state.History.Count == 0;
        }
    }

    /// <summary>尝试占用一轮生成（同一会话同时只允许一轮）</summary>
    /// <param name="sessionId">会话 ID</param>
    /// <returns>占用成功返回下一轮编号与取消令牌，否则 null（已有轮次在跑）</returns>
    internal (int Turn, CancellationToken Token)? TryBeginTurn(string sessionId) {
        lock (_gate) {
            var state = State(sessionId);
            if (state.Running) {
                return null;
            }
            state.Running = true;
            state.Turn += 1;
            state.Cancellation = new CancellationTokenSource();
            return (state.Turn, state.Cancellation.Token);
        }
    }

    /// <summary>停止该会话正在进行的生成（没有轮次在跑时返回 false）</summary>
    /// <param name="sessionId">会话 ID</param>
    /// <returns>是否真的取消了一轮生成</returns>
    internal bool Cancel(string sessionId) {
        CancellationTokenSource? cancellation;
        lock (_gate) {
            cancellation = _sessions.TryGetValue(sessionId, out var state) && state.Running ? state.Cancellation : null;
        }
        if (cancellation is null) {
            return false;
        }
        cancellation.Cancel();
        return true;
    }

    /// <summary>结束一轮生成（释放占用，允许下一轮）</summary>
    /// <param name="sessionId">会话 ID</param>
    internal void EndTurn(string sessionId) {
        CancellationTokenSource? cancellation;
        lock (_gate) {
            var state = State(sessionId);
            state.Running = false;
            cancellation = state.Cancellation;
            state.Cancellation = null;
        }
        cancellation?.Dispose();
    }

    /// <summary>当前进行中的轮次编号（空闲为 0）</summary>
    /// <param name="sessionId">会话 ID</param>
    internal int RunningTurn(string sessionId) {
        lock (_gate) {
            var state = State(sessionId);
            return state.Running ? state.Turn : 0;
        }
    }

    /// <summary>分配序号并推给该会话的全部订阅者（事件先落日志再推送，重开后序号可续）</summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="type">事件类型</param>
    /// <param name="data">事件载荷（已注册到 <see cref="DshJsonContext"/>）</param>
    /// <param name="surfaceOp">会话表面操作（消息类事件用 append）</param>
    internal async Task PublishAsync<T>(string sessionId, string type, T data, string? surfaceOp = null) {
        var (sessionEvent, targets) = Allocate(sessionId, type, data, surfaceOp);
        if (_journal is not null) {
            await _journal.AppendAsync(sessionId, sessionEvent);
        }
        foreach (var target in targets) {
            await DeliverAsync(target, PendingFor(target, sessionId));
        }
    }

    /// <summary>取某订阅者针对该会话的待发事件（同步持锁）</summary>
    private DshSessionEvent[] PendingFor(Subscription subscription, string sessionId) {
        lock (_gate) {
            var state = State(sessionId);
            return [.. state.History.Where(sessionEvent => sessionEvent.Seq > subscription.LastSeq)];
        }
    }

    /// <summary>在锁内分配序号、写入历史并快照当前订阅者（await 一律在锁外）</summary>
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
            state.History.Add(sessionEvent);
            return (sessionEvent, [.. _subscriptions.Values.Where(subscription => subscription.SessionId == sessionId)]);
        }
    }

    /// <summary>订阅者尚未收到的事件（调用方持锁；保持 seq 升序）</summary>
    private DshSessionEvent[] Pending(Subscription subscription) {
        var state = State(subscription.SessionId);
        return [.. state.History.Where(sessionEvent => sessionEvent.Seq > subscription.LastSeq)];
    }

    /// <summary>按序把一批事件推给订阅者（失败只记日志，不打断其它订阅）</summary>
    private async Task DeliverAsync(Subscription subscription, DshSessionEvent[] pending) {
        foreach (var sessionEvent in pending) {
            try {
                await subscription.Send(sessionEvent);
                subscription.LastSeq = sessionEvent.Seq;
            } catch (Exception ex) {
                App.LogDiag($"[DSH-WEB] stream push failed stream={subscription.StreamId}: {ex.Message}");
                return;
            }
        }
    }

    /// <summary>历史事件里的最大轮次（无 turn/start 为 0）</summary>
    private static int MaxTurn(IReadOnlyList<DshSessionEvent> events) =>
        events
            .Where(sessionEvent => sessionEvent.Type == "turn/start")
            .Select(sessionEvent => sessionEvent.Data.TryGetProperty("turn", out var turn) && turn.TryGetInt32(out var value) ? value : 0)
            .DefaultIfEmpty(0)
            .Max();

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
    private sealed record Subscription(string SessionId, string StreamId, Func<DshSessionEvent, Task> Send) {
        /// <summary>已推给该流的最后序号（初始为快照光标）</summary>
        internal long LastSeq { get; set; } = -1;
    }

    /// <summary>会话的事件游标与生成状态</summary>
    private sealed class SessionState {
        /// <summary>最后分配的序号（初始 -1 = 空日志）</summary>
        internal long Cursor { get; set; } = -1;
        /// <summary>最近一次轮次编号</summary>
        internal int Turn { get; set; }
        /// <summary>是否有轮次在进行</summary>
        internal bool Running { get; set; }
        /// <summary>进行中轮次的取消源（空闲为 null）</summary>
        internal CancellationTokenSource? Cancellation { get; set; }
        /// <summary>该会话的历史事件是否已从磁盘载入</summary>
        internal bool Loaded { get; set; }
        /// <summary>该会话已分配的全部事件（与日志文件一致）</summary>
        internal List<DshSessionEvent> History { get; } = [];

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
