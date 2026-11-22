namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 待确认票据队列 — 后台任务没有宿主可弹窗时，把需要用户确认的事项登记下来并立即让出等待（Defer），
/// 由前台在下一个安全点批量展示；后台不阻塞线程、不弹模态框，票据保留到前台给出 Approve/Deny。
/// </summary>
public interface IPendingConfirmationQueue {
    /// <summary>登记一条待确认事项并返回可等待的票据。</summary>
    /// <param name="agentId">发起事项的后台代理 ID（null 表示主线程）。</param>
    /// <param name="toolName">待执行工具名。</param>
    /// <param name="reason">权限管道给出的确认原因。</param>
    /// <param name="arguments">工具参数快照（用于前台展示，不应包含密钥）。</param>
    /// <returns>待确认票据；前台通过 <see cref="TryResolve"/> 结算。</returns>
    PendingConfirmationItem Enqueue(
        string? agentId,
        string toolName,
        string reason,
        IReadOnlyDictionary<string, System.Text.Json.JsonElement> arguments);

    /// <summary>只读快照（按登记时间升序），供前台批量展示；不移除票据。</summary>
    IReadOnlyList<PendingConfirmationItem> Snapshot();

    /// <summary>
    /// 结算一条待确认事项；票据不存在返回 false。
    /// <para><see cref="PendingConfirmationDecision.Defer"/> 只完成等待并保留票据供前台展示；
    /// <see cref="PendingConfirmationDecision.Approve"/>/<see cref="PendingConfirmationDecision.Deny"/> 结算并移除。</para>
    /// </summary>
    /// <param name="id">票据 ID。</param>
    /// <param name="decision">前台决策。</param>
    /// <returns>是否结算成功。</returns>
    bool TryResolve(string id, PendingConfirmationDecision decision);

    /// <summary>是否存在待确认事项。</summary>
    bool HasPending { get; }
}

/// <summary>一条待确认事项 — 前台展示用元数据 + 后台等待用异步决策。</summary>
public sealed record PendingConfirmationItem {
    /// <summary>票据 ID（稳定唯一）。</summary>
    public required string Id { get; init; }
    /// <summary>发起事项的后台代理 ID（null 表示主线程）。</summary>
    public string? AgentId { get; init; }
    /// <summary>待执行工具名。</summary>
    public required string ToolName { get; init; }
    /// <summary>权限管道给出的确认原因。</summary>
    public required string Reason { get; init; }
    /// <summary>工具参数快照。</summary>
    public required IReadOnlyDictionary<string, System.Text.Json.JsonElement> Arguments { get; init; }
    /// <summary>登记时间（UTC）。</summary>
    public DateTime CreatedAtUtc { get; init; } = DateTime.UtcNow;
    /// <summary>后台等待的前台决策；票据结算后完成。</summary>
    public required Task<PendingConfirmationDecision> Decision { get; init; }
}

/// <summary>前台对待确认事项的决策。</summary>
public enum PendingConfirmationDecision {
    /// <summary>批准执行。</summary>
    Approve,
    /// <summary>拒绝执行。</summary>
    Deny,
    /// <summary>本次不处理，交由后续回合重试。</summary>
    Defer
}
