namespace McpToolRegistry;

/// <summary>
/// 待确认票据前台结算器 — 把队列票据转换成用户可读清单，并在批准/拒绝后向发起代理发送通知。
/// <para>批准不会自动重试原调用：后台调用已按 Defer 结束并返回“待确认”，单向恢复会让代理跳过
/// 自己的回合边界。通知只告诉代理用户已复核，由代理在前台回合决定是否重试。</para>
/// </summary>
[Register(typeof(IPendingConfirmationResolver), ServiceLifetime.Singleton)]
public sealed partial class PendingConfirmationResolver : ServiceEntity, IPendingConfirmationResolver {
    private readonly IPendingConfirmationQueue _queue;
    private readonly IAgentNotificationQueue _notifications;

    /// <summary>创建结算器。</summary>
    /// <param name="queue">待确认票据队列。</param>
    /// <param name="notifications">代理通知队列（复核结果回传）。</param>
    public PendingConfirmationResolver(
        IPendingConfirmationQueue queue,
        IAgentNotificationQueue notifications) {
        _queue = queue ?? throw new ArgumentNullException(nameof(queue));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
    }

    /// <inheritdoc/>
    public IReadOnlyList<PendingConfirmationItem> ListPending() => _queue.Snapshot();

    /// <inheritdoc/>
    public bool Resolve(string id, PendingConfirmationDecision decision) {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        if (decision == PendingConfirmationDecision.Defer) {
            return false;
        }

        var item = _queue.Snapshot().FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.Ordinal));
        if (item is null || !_queue.TryResolve(id, decision)) {
            return false;
        }

        Notify(item, decision);
        Diag.WriteLine($"[PENDING-CONFIRM-RESOLVER] resolve id={id} tool={item.ToolName} decision={decision}");
        return true;
    }

    /// <inheritdoc/>
    public int ResolveAll(PendingConfirmationDecision decision) {
        if (decision == PendingConfirmationDecision.Defer) {
            return 0;
        }

        var resolved = 0;
        foreach (var item in _queue.Snapshot()) {
            if (_queue.TryResolve(item.Id, decision)) {
                Notify(item, decision);
                resolved++;
            }
        }
        Diag.WriteLine($"[PENDING-CONFIRM-RESOLVER] resolve-all decision={decision} count={resolved}");
        return resolved;
    }

    /// <summary>
    /// 把复核结果写成通知 XML 入队。目标代理 ID 为 null 时发给主线程；通知只含工具名与票据 ID，
    /// 不回传参数，避免把用户界面展示过的脱敏快照再送进模型上下文。
    /// </summary>
    private void Notify(PendingConfirmationItem item, PendingConfirmationDecision decision) {
        var verdict = decision == PendingConfirmationDecision.Approve ? "approved" : "denied";
        var instruction = decision == PendingConfirmationDecision.Approve
            ? "用户已复核通过；原后台调用已结束，如需执行请在前台交互模式重试同一操作。"
            : "用户已拒绝该操作；不要重复尝试同一调用，改用不触发确认的替代方案。";
        var xml = "<pending-confirmation-decision"
            + $" id=\"{Escape(item.Id)}\""
            + $" tool=\"{Escape(item.ToolName)}\""
            + $" decision=\"{verdict}\">"
            + Escape(instruction)
            + "</pending-confirmation-decision>";
        _notifications.Enqueue(item.AgentId, xml);
    }

    /// <summary>XML 文本转义（票据 ID/工具名/固定文案均可能被前端回传污染）。</summary>
    private static string Escape(string text) => text
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal)
        .Replace("'", "&apos;", StringComparison.Ordinal);
}
