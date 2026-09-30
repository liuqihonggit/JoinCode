namespace Core.Context;

/// <summary>
/// 对话历史存储实现 — 管理按 SessionId 分桶的 AppendOnlyLog
/// <para>单一数据源: ImmutableHamT&lt;string, AppendOnlyLog&gt; _logs</para>
/// <para>线程安全: SwitchSession 可在外部线程调用，GetOrAddLog 使用 CAS 无锁并发;</para>
/// <para>Append/CompactInPlace/TrimLastTurn/TruncateTo 由 Actor Consumer 串行调用。</para>
/// </summary>
internal sealed class SessionStore : ISessionStore {
    private string _sessionId;
    private ImmutableHamT<string, AppendOnlyLog> _logs = ImmutableHamT<string, AppendOnlyLog>.Empty;

    /// <summary>构造对话历史存储</summary>
    /// <param name="sessionId">初始会话标识</param>
    public SessionStore(string sessionId) {
        _sessionId = sessionId;
    }

    private AppendOnlyLog GetOrAddLog(string sessionId) {
        if (_logs.TryGetValue(sessionId, out var existing)) return existing;
        var newLog = new AppendOnlyLog();
        while (true) {
            var current = _logs;
            if (current.TryGetValue(sessionId, out existing)) return existing;
            var updated = current.Add(sessionId, newLog);
            if (Interlocked.CompareExchange(ref _logs, updated, current) == current) return newLog;
        }
    }

    /// <inheritdoc />
    public int Count => Log.Count;

    /// <inheritdoc />
    public AppendOnlyLog Log => GetOrAddLog(_sessionId);

    /// <inheritdoc />
    public IReadOnlyList<ApiMessage> ToMessages() => Log.ToMessages();

    /// <inheritdoc />
    public void Append(ApiMessage message) => Log.Append(message);

    /// <inheritdoc />
    public void CompactInPlace(IReadOnlyList<ApiMessage> messages) => Log.CompactInPlace(messages);

    /// <inheritdoc />
    public int TrimLastTurn() => Log.TrimLastTurn();

    /// <inheritdoc />
    public int TruncateTo(int index) => Log.TruncateTo(index);

    /// <inheritdoc />
    public void SwitchSession(string sessionId) => _sessionId = sessionId;
}
