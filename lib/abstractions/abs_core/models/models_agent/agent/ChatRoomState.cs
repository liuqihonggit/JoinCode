namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 聊天室状态 — 合并 TeamManager 中 6 个 key 相同的字典为单一类，含消息保留策略 — ADR 0109 决策13。
/// <para>对标 QQ 群数据模型：群信息 + 成员 + 消息 + 会话 + 路径权限 + 成员详情。</para>
/// <para>内存控制：MaxMessageCount 限制消息数，超过时标记 NeedsCleanup 提示用户清理，不强制删除。</para>
/// <para>按需加载：配合 IChatRoomStore 实现懒加载，默认不载入全部房间。</para>
/// </summary>
public sealed class ChatRoomState
{
    /// <summary>团队/聊天室信息</summary>
    public TeamInfo Info { get; set; } = null!;

    /// <summary>成员 ID 集合</summary>
    public HashSet<string> Members { get; set; } = new();

    /// <summary>消息字典（MessageId → Message），用 MessageId 去重 — ADR 0109 决策10。</summary>
    public ConcurrentDictionary<string, TeamMessage> Messages { get; set; } = new();

    /// <summary>会话 ID（文件邮箱需要）</summary>
    public string? SessionId { get; set; }

    /// <summary>团队级允许路径</summary>
    public Dictionary<string, TeamAllowedPath> AllowedPaths { get; set; } = new();

    /// <summary>成员详情（AgentId → TeamMemberInfo）</summary>
    public Dictionary<string, TeamMemberInfo> MemberDetails { get; set; } = new();

    /// <summary>最大消息保留数（默认 1000，对标 QQ 本地缓存）— ADR 0109 决策13。</summary>
    public int MaxMessageCount { get; init; } = 1000;

    /// <summary>是否需要清理（消息数超过 MaxMessageCount）— 不强制删除，提示用户清理。</summary>
    public bool NeedsCleanup => Messages.Count > MaxMessageCount;

    /// <summary>当前消息数</summary>
    public int MessageCount => Messages.Count;

    /// <summary>最后消息时间（null 表示无消息）</summary>
    public DateTime? LastMessageAt => Messages.Count > 0
        ? Messages.Values.Max(m => m.Timestamp)
        : null;

    /// <summary>
    /// 添加消息 — 用 MessageId 去重，重复消息不插入。
    /// <para>超过 MaxMessageCount 时不强制删除，仅标记 <see cref="NeedsCleanup"/>。</para>
    /// </summary>
    /// <param name="message">消息</param>
    /// <returns>true=新消息已插入；false=重复消息未插入</returns>
    public bool AddMessage(TeamMessage message)
        => Messages.TryAdd(message.MessageId, message);

    /// <summary>
    /// 清理旧消息 — 删除超过 <see cref="MaxMessageCount"/> 的最旧消息。
    /// <para>用户主动调用，或系统提示后用户确认清理。</para>
    /// </summary>
    /// <returns>清理的消息数</returns>
    public int CleanupOldMessages()
    {
        if (Messages.Count <= MaxMessageCount) return 0;

        var toRemove = Messages.Count - MaxMessageCount;
        var oldest = Messages.Values
            .OrderBy(m => m.Timestamp)
            .Take(toRemove)
            .Select(m => m.MessageId)
            .ToList();

        var removed = 0;
        foreach (var msgId in oldest)
        {
            if (Messages.TryRemove(msgId, out _)) removed++;
        }
        return removed;
    }

    /// <summary>
    /// 获取消息列表 — 按时间倒序，可限制条数。
    /// </summary>
    /// <param name="limit">返回上限（0=全部）</param>
    /// <returns>消息只读列表</returns>
    public IReadOnlyList<TeamMessage> GetMessages(int limit = 0)
    {
        var query = Messages.Values.OrderByDescending(m => m.Timestamp);
        return limit > 0 ? query.Take(limit).ToList() : query.ToList();
    }

    /// <summary>
    /// 获取指定可见性的消息列表 — 按时间倒序。
    /// </summary>
    /// <param name="visibility">消息可见性</param>
    /// <param name="limit">返回上限（0=全部）</param>
    /// <returns>消息只读列表</returns>
    public IReadOnlyList<TeamMessage> GetMessages(MessageVisibility visibility, int limit = 0)
    {
        var query = Messages.Values
            .Where(m => m.Visibility == visibility)
            .OrderByDescending(m => m.Timestamp);
        return limit > 0 ? query.Take(limit).ToList() : query.ToList();
    }
}

/// <summary>
/// 聊天室存储接口 — 按需加载/保存聊天室状态，避免一次性载入全部房间 — ADR 0109 决策13。
/// <para>对标 QQ 云端存档：本地只缓存活跃房间，历史房间按需从存储加载。</para>
/// </summary>
public interface IChatRoomStore
{
    /// <summary>按需加载聊天室状态（未加载时返回 null）</summary>
    System.Threading.Tasks.Task<ChatRoomState?> LoadAsync(string teamId, CancellationToken ct = default);

    /// <summary>保存聊天室状态</summary>
    System.Threading.Tasks.Task SaveAsync(string teamId, ChatRoomState state, CancellationToken ct = default);

    /// <summary>列出所有聊天室 ID（不加载全部数据，仅返回 ID 列表）</summary>
    System.Threading.Tasks.Task<IReadOnlyList<string>> ListRoomIdsAsync(CancellationToken ct = default);

    /// <summary>删除聊天室状态</summary>
    System.Threading.Tasks.Task DeleteAsync(string teamId, CancellationToken ct = default);

    /// <summary>获取需要清理的聊天室列表（消息数超过 MaxMessageCount）</summary>
    System.Threading.Tasks.Task<IReadOnlyList<(string TeamId, int MessageCount, int MaxCount)>> GetRoomsNeedingCleanupAsync(CancellationToken ct = default);
}
