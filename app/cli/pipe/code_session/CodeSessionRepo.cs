namespace JoinCode.Pipe;

/// <summary>代码会话状态</summary>
public enum CodeSessionStatus {
    /// <summary>活动状态 — 会话正在进行</summary>
    [EnumValue("active")] Active,
    /// <summary>已关闭状态 — 会话已结束</summary>
    [EnumValue("closed")] Closed
}

/// <summary>代码会话记录 — 描述一次代码会话的元数据与当前状态</summary>
public sealed class CodeSessionRecord {
    /// <summary>会话唯一标识</summary>
    public required string SessionId { get; init; }
    /// <summary>项目名称</summary>
    public required string ProjectName { get; set; }
    /// <summary>工作目录绝对路径</summary>
    public required string WorkDirectory { get; set; }
    /// <summary>会话当前状态</summary>
    public CodeSessionStatus Status { get; set; } = CodeSessionStatus.Active;
    /// <summary>会话创建时间（UTC）</summary>
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    /// <summary>会话最后更新时间（UTC）</summary>
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>代码会话仓储 — 基于内存并发字典存储会话记录，单例服务</summary>
[Register(typeof(CodeSessionRepo), ServiceLifetime.Singleton)]
public sealed partial class CodeSessionRepo : ServiceEntity {
    private readonly ConcurrentDictionary<string, CodeSessionRecord> _store = new(StringComparer.Ordinal);

    /// <summary>保存会话记录 — 按 SessionId 索引覆盖写入</summary>
    /// <param name="record">要保存的会话记录</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步保存操作的任务</returns>
    public ValueTask SaveAsync(CodeSessionRecord record, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(record);
        _store[record.SessionId] = record;
        return ValueTask.CompletedTask;
    }

    /// <summary>按会话 ID 获取会话记录</summary>
    /// <param name="sessionId">会话唯一标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>匹配的会话记录；若不存在则返回 null</returns>
    public ValueTask<CodeSessionRecord?> GetAsync(string sessionId, CancellationToken ct = default) {
        _store.TryGetValue(sessionId, out var record);
        return ValueTask.FromResult(record);
    }

    /// <summary>按会话 ID 删除会话记录</summary>
    /// <param name="sessionId">会话唯一标识</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>表示异步删除操作的任务</returns>
    public ValueTask DeleteAsync(string sessionId, CancellationToken ct = default) {
        _store.TryRemove(sessionId, out _);
        return ValueTask.CompletedTask;
    }

    /// <summary>获取所有会话记录 — 按创建时间倒序返回</summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>所有会话记录的只读列表</returns>
    public ValueTask<IReadOnlyList<CodeSessionRecord>> GetAllAsync(CancellationToken ct = default) {
        IReadOnlyList<CodeSessionRecord> result = _store.Values
            .OrderByDescending(r => r.CreatedAt)
            .ToList();
        return ValueTask.FromResult(result);
    }
}