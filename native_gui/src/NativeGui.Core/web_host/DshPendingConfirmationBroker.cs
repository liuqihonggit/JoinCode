namespace JoinCode.Gui.WebHost;

/// <summary>
/// DSH 待确认收件箱数据面 — 引擎队列/结算器就绪后注入；未就绪时列表返回空并带引导原因，
/// 结算请求 fail-closed 返回失败（不让前端误以为已处理）。
/// <para>后台任务因权限确认被 Defer 时票据进入队列；用户在前端收件箱批准/拒绝后，
/// 结算器向发起代理发送复核通知。批准不会自动恢复原调用。</para>
/// </summary>
internal sealed class DshPendingConfirmationBroker {
    private IPendingConfirmationQueue? _queue;
    private IPendingConfirmationResolver? _resolver;

    /// <summary>注入队列与结算器（引擎组装完成后调用；null 表示引擎尚未就绪）。</summary>
    /// <param name="queue">待确认票据队列。</param>
    /// <param name="resolver">前台结算器。</param>
    internal void Attach(IPendingConfirmationQueue? queue, IPendingConfirmationResolver? resolver) {
        Volatile.Write(ref _queue, queue);
        Volatile.Write(ref _resolver, resolver);
        GuiDiag.Log($"[DSH-PENDING] attached queue={queue is not null} resolver={resolver is not null}");
    }

    /// <summary>列出当前待确认项；引擎未就绪时返回空列表与引导文案。</summary>
    internal DshPendingConfirmationList List() {
        var queue = Volatile.Read(ref _queue);
        if (queue is null) {
            return new DshPendingConfirmationList {
                Available = false,
                Message = "引擎仍在组装中，待确认收件箱暂不可用；请稍后重试。",
            };
        }

        return new DshPendingConfirmationList {
            Available = true,
            Items = [.. queue.Snapshot().Select(ToDto)],
        };
    }

    /// <summary>
    /// 结算一条票据。决策只接受 <c>approve</c>/<c>deny</c>；未知值、票据不存在、引擎未就绪均返回失败原因。
    /// </summary>
    /// <param name="id">票据 ID。</param>
    /// <param name="decision">前端决策字符串。</param>
    internal DshPendingConfirmationResolveResult Resolve(string id, string decision) {
        if (Volatile.Read(ref _resolver) is not { } resolver) {
            return new DshPendingConfirmationResolveResult {
                Resolved = false,
                Message = "引擎仍在组装中，待确认收件箱暂不可用；本次未结算。",
            };
        }
        if (ToDecision(decision) is not { } parsed) {
            return new DshPendingConfirmationResolveResult {
                Resolved = false,
                Message = $"未知决策 '{decision}'；只接受 approve 或 deny。",
            };
        }
        if (!resolver.Resolve(id, parsed)) {
            return new DshPendingConfirmationResolveResult {
                Resolved = false,
                Message = $"票据 '{id}' 不存在、已过期或已被处理；请刷新收件箱后重试。",
            };
        }
        return new DshPendingConfirmationResolveResult {
            Resolved = true,
            Remaining = resolver.ListPending().Count,
        };
    }

    /// <summary>批量结算当前所有票据；引擎未就绪或决策非法返回 0 条。</summary>
    internal DshPendingConfirmationResolveResult ResolveAll(string decision) {
        if (Volatile.Read(ref _resolver) is not { } resolver) {
            return new DshPendingConfirmationResolveResult {
                Resolved = false,
                Message = "引擎仍在组装中，待确认收件箱暂不可用；本次未结算。",
            };
        }
        if (ToDecision(decision) is not { } parsed) {
            return new DshPendingConfirmationResolveResult {
                Resolved = false,
                Message = $"未知决策 '{decision}'；只接受 approve 或 deny。",
            };
        }

        var resolved = resolver.ResolveAll(parsed);
        return new DshPendingConfirmationResolveResult {
            Resolved = resolved > 0,
            Remaining = resolver.ListPending().Count,
            Message = resolved == 0 ? "当前没有可结算的待确认项。" : $"已结算 {resolved} 条。",
        };
    }

    /// <summary>前端决策字符串 → 队列决策；只暴露 approve/deny（Defer 由后台自动完成，不允许前端触发）。</summary>
    private static PendingConfirmationDecision? ToDecision(string decision) =>
        decision switch {
            "approve" => PendingConfirmationDecision.Approve,
            "deny" => PendingConfirmationDecision.Deny,
            _ => null,
        };

    /// <summary>票据 → 前端 DTO（参数已由队列脱敏，时间转 Unix 毫秒）。</summary>
    private static DshPendingConfirmationItem ToDto(PendingConfirmationItem item) => new() {
        Id = item.Id,
        AgentId = item.AgentId,
        ToolName = item.ToolName,
        Reason = item.Reason,
        Arguments = new Dictionary<string, System.Text.Json.JsonElement>(item.Arguments, StringComparer.Ordinal),
        CreatedAt = new DateTimeOffset(item.CreatedAtUtc, TimeSpan.Zero).ToUnixTimeMilliseconds(),
    };
}
