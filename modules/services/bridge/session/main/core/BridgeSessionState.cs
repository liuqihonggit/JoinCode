namespace Core.Bridge;

/// <summary>
/// Bridge 会话状态 — 合并 8 个以 sessionId 为 key 的字典为单一不可变 record
/// 并发更新用 ConcurrentDictionary.AddOrUpdate + record with 原子交换
/// </summary>
internal sealed record BridgeSessionState {
    /// <summary>子进程句柄（注册时由 SpawnMiddleware 保证非 null，测试可传 null）</summary>
    public BridgeSubprocessHandle? Handle { get; init; }

    /// <summary>会话启动时间</summary>
    public required DateTime StartTime { get; init; }

    /// <summary>工作 ID</summary>
    public required string WorkId { get; init; }

    /// <summary>入口令牌（可选）</summary>
    public string? IngressToken { get; init; }

    /// <summary>worktree 路径（可选）</summary>
    public string? WorktreePath { get; init; }

    /// <summary>兼容 ID（可选）</summary>
    public string? CompatId { get; init; }

    /// <summary>是否已超时</summary>
    public bool IsTimedOut { get; init; }

    /// <summary>是否为 V2 会话</summary>
    public bool IsV2 { get; init; }
}