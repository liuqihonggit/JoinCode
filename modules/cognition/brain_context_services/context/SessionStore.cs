namespace Core.Context;

/// <summary>
/// 对话历史存储实现 — 管理按 SessionId 分桶的 AppendOnlyLog
/// <para>单一数据源: ImmutableHamT&lt;string, AppendOnlyLog&gt; _logs</para>
/// <para>线程安全: SwitchSession 可在外部线程调用，GetOrAddLog 使用 CAS 无锁并发;</para>
/// <para>Append/CompactInPlace/TrimLastTurn/TruncateTo 由 Actor Consumer 串行调用。</para>
/// <para>ADR 0167: 全部成员支持显式 sessionId（null = 当前指针）— 在途查询按出生时的会话代次读写，
/// /clear 换指针后不会把它搬去新代。</para>
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

    /// <summary>显式 sessionId 解析 — null/空时用当前指针（ADR 0167）。</summary>
    private string Resolve(string? sessionId) =>
        string.IsNullOrEmpty(sessionId) ? _sessionId : sessionId;

    /// <inheritdoc />
    public int Count => CountOf(null);

    /// <inheritdoc />
    public AppendOnlyLog Log => GetLog(null);

    /// <inheritdoc />
    public IReadOnlyList<ApiMessage> ToMessages() => ToMessages(null);

    /// <inheritdoc />
    public void Append(ApiMessage message) => Append(message, null);

    /// <inheritdoc />
    public void CompactInPlace(IReadOnlyList<ApiMessage> messages) => CompactInPlace(messages, null);

    /// <inheritdoc />
    public int TrimLastTurn() => TrimLastTurn(null);

    /// <inheritdoc />
    public int TruncateTo(int index) => TruncateTo(index, null);

    /// <inheritdoc />
    public void SwitchSession(string sessionId) => _sessionId = sessionId;

    /// <inheritdoc />
    public int CountOf(string? sessionId) => GetOrAddLog(Resolve(sessionId)).Count;

    /// <inheritdoc />
    public AppendOnlyLog GetLog(string? sessionId) => GetOrAddLog(Resolve(sessionId));

    /// <inheritdoc />
    public IReadOnlyList<ApiMessage> ToMessages(string? sessionId) => GetOrAddLog(Resolve(sessionId)).ToMessages();

    /// <inheritdoc />
    public void Append(ApiMessage message, string? sessionId) => GetOrAddLog(Resolve(sessionId)).Append(message);

    /// <inheritdoc />
    public void CompactInPlace(IReadOnlyList<ApiMessage> messages, string? sessionId) =>
        GetOrAddLog(Resolve(sessionId)).CompactInPlace(messages);

    /// <inheritdoc />
    public int TrimLastTurn(string? sessionId) => GetOrAddLog(Resolve(sessionId)).TrimLastTurn();

    /// <inheritdoc />
    public int TruncateTo(int index, string? sessionId) => GetOrAddLog(Resolve(sessionId)).TruncateTo(index);

    /// <inheritdoc />
    public bool ReleaseSession(string sessionId) {
        if (string.IsNullOrEmpty(sessionId)) return false;
        while (true) {
            var current = _logs;
            if (!current.TryGetValue(sessionId, out _)) return false;
            var updated = current.Remove(sessionId);
            if (Interlocked.CompareExchange(ref _logs, updated, current) == current) return true;
        }
    }
}
