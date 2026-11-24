namespace Core.Context;

/// <summary>
/// 会话启动 Hook 注入的上下文缓存（TASK130-A6）。
/// <para>
/// 为什么需要这个 store：SessionStart hook 在 <c>chatinit-pipeline</c> 里只执行一次，而
/// <c>SystemPromptMiddleware</c> 每回合开头都会 <c>ClearDynamicSystemMessagesAsync</c> 再重建提示词，
/// 所以「init 时写一条动态系统消息」会在首轮就被清掉。写会话日志（<c>AddSystemMessageAsync</c>）
/// 也不行 —— 恢复会话时 <c>ContextPersistenceService.LoadAsync</c> 会把 system 消息当静态提示词处理。
/// 故把文本暂存于此，由 <see cref="SessionStartContextMiddleware"/> 每回合补写。
/// </para>
/// </summary>
public interface ISessionStartContextStore {
    /// <summary>写入指定会话的启动上下文；传 null/空白表示清空（防止上一轮残留）。</summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="text">要注入的上下文文本。</param>
    void Set(string sessionId, string? text);

    /// <summary>取指定会话的启动上下文；sessionId 不匹配返回 null（防串会话）。</summary>
    /// <param name="sessionId">会话标识。</param>
    string? Get(string sessionId);
}

/// <inheritdoc />
/// <remarks>
/// 单写（会话初始化）多读（每回合预处理）场景，用不可变槽位 + <see cref="Volatile"/> 换引用，
/// 不引入锁原语（JCC4008 基线只减不增）。
/// </remarks>
[Register(typeof(ISessionStartContextStore), ServiceLifetime.Singleton)]
public sealed partial class SessionStartContextStore : ServiceEntity, ISessionStartContextStore {
    private sealed record Slot(string SessionId, string Text);

    private Slot? _slot;

    /// <inheritdoc />
    public void Set(string sessionId, string? text) {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        var normalized = text?.Trim();
        Volatile.Write(
            ref _slot,
            string.IsNullOrEmpty(normalized) ? null : new Slot(sessionId, normalized));
    }

    /// <inheritdoc />
    public string? Get(string sessionId) {
        var slot = Volatile.Read(ref _slot);
        return slot is not null && string.Equals(slot.SessionId, sessionId, StringComparison.Ordinal)
            ? slot.Text
            : null;
    }
}
