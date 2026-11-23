namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 待确认票据的前台结算入口 — DSH Web 与 TUI 共用：列出待确认项、逐条或批量批准/拒绝，
/// 并把复核结果作为通知回传给发起票据的代理，避免后台任务永远不知道用户已处理。
/// <para>批准只代表“复核通过”，不会自动恢复原后台调用（原调用已按 Defer 结束）；代理收到通知后
/// 可在前台交互模式重试。<see cref="PendingConfirmationDecision.Defer"/> 不接受，返回 false。</para>
/// </summary>
public interface IPendingConfirmationResolver {
    /// <summary>列出当前可见的待确认票据（已按登记时间升序，过期项已清理）。</summary>
    IReadOnlyList<PendingConfirmationItem> ListPending();

    /// <summary>结算一张票据，并把结果通知发起代理。</summary>
    /// <param name="id">票据 ID。</param>
    /// <param name="decision">批准或拒绝；Defer 返回 false。</param>
    /// <returns>是否结算成功（票据不存在/已过期/只允许 Defer 时为 false）。</returns>
    bool Resolve(string id, PendingConfirmationDecision decision);

    /// <summary>批量结算当前所有票据（用于“全部批准/全部拒绝”）。</summary>
    /// <param name="decision">批准或拒绝；Defer 返回 0。</param>
    /// <returns>实际结算条数。</returns>
    int ResolveAll(PendingConfirmationDecision decision);
}
