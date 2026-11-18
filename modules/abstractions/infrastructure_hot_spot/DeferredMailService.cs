namespace Infrastructure.HotSpot;

/// <summary>
/// 延迟邮件服务实现 — 不可变快照 + CAS 线程安全
/// 轮次计数到期或任务结束注入时投递
/// </summary>
[Register(typeof(IDeferredMailService), ServiceLifetime.Singleton)]
public sealed class DeferredMailService : IDeferredMailService {
    private ImmutableHamT<string, ImmutableList<DeferredMailEntry>> _pending = ImmutableHamT<string, ImmutableList<DeferredMailEntry>>.Empty;

    /// <summary>
    /// 延迟投递邮件 — 加入待发送队列，按 OpenAfterTurns 计数到期后投递
    /// </summary>
    /// <param name="mail">延迟邮件</param>
    /// <param name="cancellationToken">取消令牌</param>
    public Task DeferAsync(DeferredMail mail, CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(mail);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = new DeferredMailEntry { Mail = mail, RemainingTurns = mail.OpenAfterTurns };
        Publish(mail.To, list => list.Add(entry));
        return Task.CompletedTask;
    }

    /// <summary>
    /// 推进一轮轮次计数，返回已到期的邮件列表
    /// </summary>
    /// <param name="agentId">目标 Agent 标识</param>
    /// <returns>已到期可投递的邮件列表</returns>
    public IReadOnlyList<DeferredMail> TickTurns(string agentId) {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        while (true) {
            var current = Volatile.Read(ref _pending);
            if (!current.TryGetValue(agentId, out var list))
                return [];

            var matured = new List<DeferredMail>();
            var remaining = ImmutableList<DeferredMailEntry>.Empty;
            foreach (var entry in list) {
                if (entry.RemainingTurns - 1 <= 0)
                    matured.Add(entry.Mail);
                else
                    remaining = remaining.Add(entry with { RemainingTurns = entry.RemainingTurns - 1 });
            }

            var next = remaining.Count == 0 ? current.Remove(agentId) : current.SetItem(agentId, remaining);
            if (Interlocked.CompareExchange(ref _pending, next, current) == current)
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

        while (true) {
            var current = Volatile.Read(ref _pending);
            if (!current.TryGetValue(agentId, out var list))
                return [];

            List<DeferredMail> flushed;
            ImmutableHamT<string, ImmutableList<DeferredMailEntry>> next;

            if (markerFilter is { } filter) {
                flushed = list.Where(e => e.Mail.Marker.HasFlag(filter)).Select(e => e.Mail).ToList();
                var remaining = list.RemoveAll(e => e.Mail.Marker.HasFlag(filter));
                next = remaining.Count == 0 ? current.Remove(agentId) : current.SetItem(agentId, remaining);
            } else {
                flushed = list.Select(e => e.Mail).ToList();
                next = current.Remove(agentId);
            }

            if (Interlocked.CompareExchange(ref _pending, next, current) == current)
                return flushed;
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
        if (!Volatile.Read(ref _pending).TryGetValue(agentId, out var list))
            return [];
        var mails = list.Select(e => e.Mail);
        if (markerFilter is { } filter)
            mails = mails.Where(m => m.Marker.HasFlag(filter));
        return mails.ToList();
    }

    private void Publish(string agentId, Func<ImmutableList<DeferredMailEntry>, ImmutableList<DeferredMailEntry>> mutate) {
        while (true) {
            var current = Volatile.Read(ref _pending);
            var list = current.GetValueOrDefault(agentId, ImmutableList<DeferredMailEntry>.Empty);
            var next = current.SetItem(agentId, mutate(list));
            if (Interlocked.CompareExchange(ref _pending, next, current) == current)
                return;
        }
    }

    private sealed record DeferredMailEntry {
        /// <summary>获取延迟邮件。</summary>
        public required DeferredMail Mail { get; init; }
        /// <summary>获取剩余轮次。</summary>
        public required int RemainingTurns { get; init; }
    }
}