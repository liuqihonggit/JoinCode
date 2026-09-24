namespace JoinCode.Abstractions.Models.Agent;

/// <summary>
/// 聊天室状态 — 合并 TeamManager 中 6 个 key 相同的字典为单一类，含消息保留策略 — ADR 0109 决策13。
/// <para>对标 QQ 群数据模型：群信息 + 成员 + 消息 + 会话 + 路径权限 + 成员详情。</para>
/// <para>内存控制：MaxMessageCount 限制消息数，超过时标记 NeedsCleanup 提示用户清理，不强制删除。</para>
/// <para>按需加载：配合 IChatRoomStore 实现懒加载，默认不载入全部房间。</para>
/// <para>不可变 record + 不可变集合，修改通过 with 表达式返回新实例，并行检索安全。</para>
/// <para>双索引消息存储: Messages(MessageId→Message, O(1)去重/查找) + MessagesByTime(按Timestamp排序, O(1)取最新/O(limit)逆序遍历/O(log n)二分插入)。</para>
/// </summary>
public sealed record ChatRoomState {
    private static readonly IComparer<TeamMessage> s_timeComparer = Comparer<TeamMessage>.Create(static (a, b) => a.Timestamp.CompareTo(b.Timestamp));

    /// <summary>团队/聊天室信息</summary>
    public TeamInfo Info { get; init; } = null!;

    /// <summary>成员 ID 集合（不可变，O(1) 包含查询）</summary>
    public ImmutableHashSet<string> Members { get; init; } = ImmutableHashSet<string>.Empty;

    /// <summary>消息字典（MessageId → Message），O(1) 去重检查 + O(1) 按 MessageId 查找 — ADR 0109 决策10。</summary>
    public ImmutableDictionary<string, TeamMessage> Messages { get; init; } = ImmutableDictionary<string, TeamMessage>.Empty;

    /// <summary>按 Timestamp 升序排序的消息列表，O(1) 取最新/最旧 + O(limit) 逆序遍历 + O(log n) 二分法插入 — 制造排序条件提升检索效率。</summary>
    public ImmutableList<TeamMessage> MessagesByTime { get; init; } = ImmutableList<TeamMessage>.Empty;

    /// <summary>会话 ID（文件邮箱需要）</summary>
    public string? SessionId { get; init; }

    /// <summary>团队级允许路径（不可变，Path → TeamAllowedPath）</summary>
    public ImmutableDictionary<string, TeamAllowedPath> AllowedPaths { get; init; } = ImmutableDictionary<string, TeamAllowedPath>.Empty;

    /// <summary>成员详情（AgentId → TeamMemberInfo，不可变）</summary>
    public ImmutableDictionary<string, TeamMemberInfo> MemberDetails { get; init; } = ImmutableDictionary<string, TeamMemberInfo>.Empty;

    /// <summary>最大消息保留数（默认 1000，对标 QQ 本地缓存）— ADR 0109 决策13。</summary>
    public int MaxMessageCount { get; init; } = 1000;

    /// <summary>是否需要清理（消息数超过 MaxMessageCount）— 不强制删除，提示用户清理。</summary>
    public bool NeedsCleanup => Messages.Count > MaxMessageCount;

    /// <summary>当前消息数</summary>
    public int MessageCount => Messages.Count;

    /// <summary>最后消息时间（null 表示无消息）— O(1) 取 MessagesByTime 末尾元素</summary>
    public DateTime? LastMessageAt => MessagesByTime.Count > 0 ? MessagesByTime[^1].Timestamp : null;

    /// <summary>
    /// 尝试添加消息 — 用 MessageId 去重，重复消息不插入。O(log n) 二分法插入 MessagesByTime。
    /// <para>超过 MaxMessageCount 时不强制删除，仅标记 <see cref="NeedsCleanup"/>。</para>
    /// </summary>
    /// <param name="message">消息</param>
    /// <returns>(新状态, true=新消息已插入 / false=重复消息未插入)</returns>
    public (ChatRoomState State, bool Added) TryAddMessage(TeamMessage message) {
        if (Messages.ContainsKey(message.MessageId)) return (this, false);
        var newMessages = Messages.Add(message.MessageId, message);
        var newByTime = InsertByTime(MessagesByTime, message);
        return (this with { Messages = newMessages, MessagesByTime = newByTime }, true);
    }

    /// <summary>
    /// 替换消息（撤回场景）— O(n) 查找旧位置 + O(log n) 插入新位置。低频操作可接受 O(n)。
    /// </summary>
    /// <param name="messageId">要替换的消息 ID</param>
    /// <param name="newMessage">新消息内容</param>
    /// <returns>新状态（消息不存在则返回原状态）</returns>
    public ChatRoomState ReplaceMessage(string messageId, TeamMessage newMessage) {
        if (!Messages.TryGetValue(messageId, out var oldMessage)) return this;
        var newMessages = Messages.SetItem(messageId, newMessage);
        var oldIndex = MessagesByTime.IndexOf(oldMessage);
        var newByTime = oldIndex >= 0 ? MessagesByTime.RemoveAt(oldIndex) : MessagesByTime;
        newByTime = InsertByTime(newByTime, newMessage);
        return this with { Messages = newMessages, MessagesByTime = newByTime };
    }

    /// <summary>
    /// 批量设置消息（持久化加载）— O(n log n) 排序构建 MessagesByTime。
    /// </summary>
    public ChatRoomState WithMessages(IEnumerable<TeamMessage> messages) {
        var byId = messages.ToImmutableDictionary(m => m.MessageId);
        var byTime = byId.Values.OrderBy(static m => m.Timestamp).ToImmutableList();
        return this with { Messages = byId, MessagesByTime = byTime };
    }

    /// <summary>
    /// 清理旧消息 — 删除超过 <see cref="MaxMessageCount"/> 的最旧消息。O(toRemove) 取前 N 个。
    /// <para>用户主动调用，或系统提示后用户确认清理。</para>
    /// </summary>
    /// <returns>(新状态, 清理的消息数)</returns>
    public (ChatRoomState State, int RemovedCount) CleanupOldMessages() {
        if (Messages.Count <= MaxMessageCount) return (this, 0);

        var toRemove = Messages.Count - MaxMessageCount;
        var oldestIds = MessagesByTime.Take(toRemove).Select(static m => m.MessageId).ToList();
        var newMessages = Messages.RemoveRange(oldestIds);
        var newByTime = MessagesByTime.RemoveRange(0, toRemove);
        return (this with { Messages = newMessages, MessagesByTime = newByTime }, toRemove);
    }

    /// <summary>
    /// 获取消息列表 — 按时间倒序，可限制条数。O(limit) 逆序遍历 MessagesByTime。
    /// </summary>
    /// <param name="limit">返回上限（0=全部）</param>
    /// <returns>消息只读列表</returns>
    public IReadOnlyList<TeamMessage> GetMessages(int limit = 0) {
        if (MessagesByTime.Count == 0) return Array.Empty<TeamMessage>();
        var take = limit <= 0 ? MessagesByTime.Count : Math.Min(limit, MessagesByTime.Count);
        var result = new List<TeamMessage>(take);
        for (var i = MessagesByTime.Count - 1; i >= 0 && result.Count < take; i--) {
            result.Add(MessagesByTime[i]);
        }
        return result;
    }

    /// <summary>
    /// 获取指定可见性的消息列表 — 按时间倒序。O(n) 最坏逆序遍历过滤。
    /// </summary>
    /// <param name="visibility">消息可见性</param>
    /// <param name="limit">返回上限（0=全部）</param>
    /// <returns>消息只读列表</returns>
    public IReadOnlyList<TeamMessage> GetMessages(MessageVisibility visibility, int limit = 0) {
        var result = new List<TeamMessage>();
        for (var i = MessagesByTime.Count - 1; i >= 0; i--) {
            if (MessagesByTime[i].Visibility != visibility) continue;
            result.Add(MessagesByTime[i]);
            if (limit > 0 && result.Count >= limit) break;
        }
        return result;
    }

    private static ImmutableList<TeamMessage> InsertByTime(ImmutableList<TeamMessage> list, TeamMessage message) {
        var index = list.BinarySearch(message, s_timeComparer);
        if (index < 0) index = ~index;
        return list.Insert(index, message);
    }
}

/// <summary>
/// 聊天室存储接口 — 按需加载/保存聊天室状态，避免一次性载入全部房间 — ADR 0109 决策13。
/// <para>对标 QQ 云端存档：本地只缓存活跃房间，历史房间按需从存储加载。</para>
/// </summary>
public interface IChatRoomStore {
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
