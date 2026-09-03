namespace Infrastructure.HotSpot;

/// <summary>
/// 延迟邮件服务实现 — ConcurrentDictionary + per-agent lock 线程安全
/// 轮次计数到期或任务结束注入时投递
/// </summary>
[Register(typeof(IDeferredMailService), ServiceLifetime.Singleton)]
public sealed class DeferredMailService : IDeferredMailService
{
    private readonly ConcurrentDictionary<string, List<DeferredMailEntry>> _pending = new();
    private readonly ConcurrentDictionary<string, AsyncLock> _locks = new();

    public Task DeferAsync(DeferredMail mail, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mail);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = new DeferredMailEntry { Mail = mail, RemainingTurns = mail.OpenAfterTurns };
        using (GetLock(mail.To).TryLock() ?? throw new System.TimeoutException("锁等待超时"))
        {
            _pending.GetOrAdd(mail.To, _ => []).Add(entry);
        }
        return Task.CompletedTask;
    }

    public IReadOnlyList<DeferredMail> TickTurns(string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        using (GetLock(agentId).TryLock() ?? throw new System.TimeoutException("锁等待超时"))
        {
            if (!_pending.TryGetValue(agentId, out var list))
                return [];

            var matured = new List<DeferredMail>();
            var remaining = new List<DeferredMailEntry>();
            foreach (var entry in list)
            {
                entry.RemainingTurns--;
                if (entry.RemainingTurns <= 0)
                    matured.Add(entry.Mail);
                else
                    remaining.Add(entry);
            }
            list.Clear();
            list.AddRange(remaining);
            return matured;
        }
    }

    public IReadOnlyList<DeferredMail> FlushOnTaskEnd(string agentId, MailMarker? markerFilter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        using (GetLock(agentId).TryLock() ?? throw new System.TimeoutException("锁等待超时"))
        {
            if (!_pending.TryGetValue(agentId, out var list))
                return [];

            if (markerFilter is { } filter)
            {
                var matched = list.Where(e => e.Mail.Marker.HasFlag(filter)).Select(e => e.Mail).ToList();
                list.RemoveAll(e => e.Mail.Marker.HasFlag(filter));
                return matched;
            }

            var all = list.Select(e => e.Mail).ToList();
            list.Clear();
            return all;
        }
    }

    public IReadOnlyList<DeferredMail> GetPending(string agentId, MailMarker? markerFilter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        using (GetLock(agentId).TryLock() ?? throw new System.TimeoutException("锁等待超时"))
        {
            if (!_pending.TryGetValue(agentId, out var list))
                return [];
            var mails = list.Select(e => e.Mail);
            if (markerFilter is { } filter)
                mails = mails.Where(m => m.Marker.HasFlag(filter));
            return mails.ToList();
        }
    }

    private AsyncLock GetLock(string agentId) => _locks.GetOrAdd(agentId, _ => new AsyncLock("DeferredMailService"));

    private sealed class DeferredMailEntry
    {
        public required DeferredMail Mail { get; init; }
        public int RemainingTurns { get; set; }
    }
}
