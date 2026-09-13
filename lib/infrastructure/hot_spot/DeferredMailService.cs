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

    /// <summary>
    /// 延迟投递邮件 — 加入待发送队列，按 OpenAfterTurns 计数到期后投递
    /// </summary>
    /// <param name="mail">延迟邮件</param>
    /// <param name="cancellationToken">取消令牌</param>
    public Task DeferAsync(DeferredMail mail, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(mail);
        cancellationToken.ThrowIfCancellationRequested();

        var entry = new DeferredMailEntry { Mail = mail, RemainingTurns = mail.OpenAfterTurns };
        var lk = GetLock(mail.To);
        using (lk.TryLock() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时"))
        {
            _pending.GetOrAdd(mail.To, _ => []).Add(entry);
        }
        return Task.CompletedTask;
    }

    /// <summary>
    /// 推进一轮轮次计数，返回已到期的邮件列表
    /// </summary>
    /// <param name="agentId">目标 Agent 标识</param>
    /// <returns>已到期可投递的邮件列表</returns>
    public IReadOnlyList<DeferredMail> TickTurns(string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        var lk = GetLock(agentId);
        using (lk.TryLock() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时"))
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

    /// <summary>
    /// 任务结束时一次性投递所有待发送邮件，可按 MailMarker 过滤
    /// </summary>
    /// <param name="agentId">目标 Agent 标识</param>
    /// <param name="markerFilter">邮件标记过滤器；为 null 时投递全部</param>
    /// <returns>已投递的邮件列表</returns>
    public IReadOnlyList<DeferredMail> FlushOnTaskEnd(string agentId, MailMarker? markerFilter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        var lk = GetLock(agentId);
        using (lk.TryLock() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时"))
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

    /// <summary>
    /// 查询待发送邮件，可按 MailMarker 过滤
    /// </summary>
    /// <param name="agentId">目标 Agent 标识</param>
    /// <param name="markerFilter">邮件标记过滤器；为 null 时返回全部</param>
    /// <returns>待发送邮件列表</returns>
    public IReadOnlyList<DeferredMail> GetPending(string agentId, MailMarker? markerFilter = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        var lk = GetLock(agentId);
        using (lk.TryLock() ?? throw new System.TimeoutException($"锁 '{lk.Name}' 等待超时"))
        {
            if (!_pending.TryGetValue(agentId, out var list))
                return [];
            var mails = list.Select(e => e.Mail);
            if (markerFilter is { } filter)
                mails = mails.Where(m => m.Marker.HasFlag(filter));
            return mails.ToList();
        }
    }

    private AsyncLock GetLock(string agentId) => _locks.GetOrAdd(agentId, _ => new AsyncLock(nameof(DeferredMailService)));

    private sealed class DeferredMailEntry
    {
        public required DeferredMail Mail { get; init; }
        public int RemainingTurns { get; set; }
    }
}
