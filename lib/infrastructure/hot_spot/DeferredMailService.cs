namespace Infrastructure.HotSpot;

/// <summary>
/// 延迟邮件服务实现 — ConcurrentDictionary + per-agent lock 线程安全
/// 轮次计数到期或任务结束注入时投递
/// </summary>
[Register(typeof(IDeferredMailService), ServiceLifetime.Singleton)]
public sealed class DeferredMailService : IDeferredMailService {
    private ImmutableDictionary<string, ImmutableList<DeferredMailEntry>> _pending = ImmutableDictionary<string, ImmutableList<DeferredMailEntry>>.Empty;
    private ImmutableDictionary<string, AsyncLock> _locks = ImmutableDictionary<string, AsyncLock>.Empty;

    /// <summary>
    /// 延迟投递邮件 — 加入待发送队列，按 OpenAfterTurns 计数到期后投递
    /// </summary>
    /// <param name="mail">延迟邮件</param>
    /// <param name="cancellationToken">取消令牌</param>
    public Task DeferAsync(DeferredMail mail, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(mail);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = new DeferredMailEntry { Mail = mail, RemainingTurns = mail.OpenAfterTurns };
        var lk = GetLock(mail.To);
        using (lk.TryLock() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时")) {
            var list = Volatile.Read(ref _pending).GetValueOrDefault(mail.To, ImmutableList<DeferredMailEntry>.Empty);
            _pending = Volatile.Read(ref _pending).SetItem(mail.To, list.Add(entry));
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 推进一轮轮次计数，返回已到期的邮件列表
    /// </summary>
    /// <param name="agentId">目标 Agent 标识</param>
    /// <returns>已到期可投递的邮件列表</returns>
    public IReadOnlyList<DeferredMail> TickTurns(string agentId) {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        var lk = GetLock(agentId);
        using (lk.TryLock() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时")) {
            var snapshot = Volatile.Read(ref _pending);
            if (!snapshot.TryGetValue(agentId, out var list))
                return [];

            var matured = new List<DeferredMail>();
            var remaining = ImmutableList<DeferredMailEntry>.Empty;
            foreach (var entry in list) {
                entry.RemainingTurns--;
                if (entry.RemainingTurns <= 0)
                    matured.Add(entry.Mail);
                else
                    remaining = remaining.Add(entry);
            }
            _pending = snapshot.SetItem(agentId, remaining);
            return matured;
        }
    }

    /// <summary>
    /// 任务结束时一次性投递所有待发送邮件，可按 MailMarker 过滤
    /// </summary>
    /// <param name="agentId">目标 Agent 标识</param>
    /// <param name="markerFilter">邮件标记过滤器；为 null 时投递全部</param>
    /// <returns>已投递的邮件列表</returns>
    public IReadOnlyList<DeferredMail> FlushOnTaskEnd(string agentId, MailMarker? markerFilter = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        var lk = GetLock(agentId);
        using (lk.TryLock() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时")) {
            var snapshot = Volatile.Read(ref _pending);
            if (!snapshot.TryGetValue(agentId, out var list))
                return [];

            if (markerFilter is { } filter) {
                var matched = list.Where(e => e.Mail.Marker.HasFlag(filter)).Select(e => e.Mail).ToList();
                _pending = snapshot.SetItem(agentId, list.RemoveAll(e => e.Mail.Marker.HasFlag(filter)));
                return matched;
            }

            var all = list.Select(e => e.Mail).ToList();
            _pending = snapshot.Remove(agentId);
            return all;
        }
    }

    /// <summary>
    /// 查询待发送邮件，可按 MailMarker 过滤
    /// </summary>
    /// <param name="agentId">目标 Agent 标识</param>
    /// <param name="markerFilter">邮件标记过滤器；为 null 时返回全部</param>
    /// <returns>待发送邮件列表</returns>
    public IReadOnlyList<DeferredMail> GetPending(string agentId, MailMarker? markerFilter = null) {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        var lk = GetLock(agentId);
        using (lk.TryLock() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时")) {
            if (!Volatile.Read(ref _pending).TryGetValue(agentId, out var list))
                return [];
            var mails = list.Select(e => e.Mail);
            if (markerFilter is { } filter)
                mails = mails.Where(m => m.Marker.HasFlag(filter));
            return mails.ToList();
        }
    }

    private AsyncLock GetLock(string agentId) {
        var snapshot = Volatile.Read(ref _locks);
        if (snapshot.TryGetValue(agentId, out var existing))
            return existing;

        var newLock = new AsyncLock(nameof(DeferredMailService));
        ImmutableInterlocked.Update(ref _locks, d => d.ContainsKey(agentId) ? d : d.Add(agentId, newLock));
        return Volatile.Read(ref _locks)[agentId];
    }

    private sealed class DeferredMailEntry {
        /// <summary>获取延迟邮件。</summary>
        public required DeferredMail Mail { get; init; }
        /// <summary>获取或设置剩余轮次。</summary>
        public int RemainingTurns { get; set; }
    }
}