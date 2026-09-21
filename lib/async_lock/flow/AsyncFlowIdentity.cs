namespace Core.Utils;

/// <summary>
/// 异步流身份 — 统一传递 FlowId(死锁检测) + ActorId(循环 Ask 检测)。
/// <para>
/// 用单个 AsyncLocal 传递,减少 AsyncLocal 数量。FlowId 和 ActorId 独立设置互不影响:
/// SetFlowId 保留当前 ActorId,SetActorId 保留当前 FlowId。
/// </para>
/// <para>
/// FlowId 在 AsyncLock 入口惰性注册(EnsureFlowRegistered),ActorId 在 ActorBase.ConsumeLoopAsync 入口设置。
/// </para>
/// </summary>
public sealed class AsyncFlowIdentity {
    /// <summary>获取异步流标识(0 表示未注册)。</summary>
    public int FlowId { get; init; }
    /// <summary>获取 Actor 标识(null 表示非 Actor 上下文)。</summary>
    public string? ActorId { get; init; }

    private static readonly AsyncLocal<AsyncFlowIdentity?> _current = new();

    /// <summary>当前异步流身份(null 表示未设置)</summary>
    public static AsyncFlowIdentity? Current => _current.Value;

    /// <summary>当前 FlowId(0 表示未注册)</summary>
    public static int CurrentFlowId => _current.Value?.FlowId ?? 0;

    /// <summary>当前 ActorId(null 表示非 Actor 上下文)</summary>
    public static string? CurrentActorId => _current.Value?.ActorId;

    /// <summary>仅设置 FlowId,保留当前 ActorId</summary>
    public static void SetFlowId(int flowId) {
        var prev = _current.Value;
        _current.Value = new AsyncFlowIdentity { FlowId = flowId, ActorId = prev?.ActorId };
    }

    /// <summary>仅设置 ActorId,保留当前 FlowId</summary>
    public static void SetActorId(string? actorId) {
        var prev = _current.Value;
        _current.Value = new AsyncFlowIdentity { FlowId = prev?.FlowId ?? 0, ActorId = actorId };
    }

    /// <summary>清除 ActorId(保留 FlowId)— Actor Consumer 退出时调用</summary>
    public static void ClearActorId() => SetActorId(null);

    /// <summary>
    /// 进入 Actor 作用域 — 设置 ActorId(保留当前 FlowId),返回 scope(Dispose 时恢复原值)。
    /// 比 SetActorId+try-finally+ClearActorId 更安全:正确处理嵌套 Actor(恢复外层 ActorId 而非置空)。
    /// </summary>
    public static IDisposable EnterActorScope(string actorId) {
        var prev = _current.Value;
        _current.Value = new AsyncFlowIdentity { FlowId = prev?.FlowId ?? 0, ActorId = actorId };
        return new ActorScope(_current, prev);
    }

    /// <summary>Actor 作用域 — Dispose 时恢复原 AsyncFlowIdentity(正确处理嵌套 Actor)</summary>
    private sealed class ActorScope(AsyncLocal<AsyncFlowIdentity?> store, AsyncFlowIdentity? previous) : IDisposable {
        /// <summary>释放资源。</summary>
        public void Dispose() => store.Value = previous;
    }

    /// <summary>清除全部 — 测试用</summary>
    public static void Clear() => _current.Value = null;
}