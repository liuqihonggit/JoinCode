namespace JoinCode.Gui.WebView;

/// <summary>
/// 权限确认弹窗代理（P6-5/F2）：把引擎的 <see cref="PermissionConfirmationRequest"/> 转成桥接消息下发 web，
/// 等待 web 的 permission/response 回传后解析为 <see cref="PermissionConfirmationDecision"/>。
/// <para>安全方向（fail-closed）：超时、窗口关闭、令牌取消、未知请求 Id、非法决策值一律按 Deny 收口。</para>
/// <para>并发：一次会话可有多条权限请求在途（子代理并行），按请求 Id 分桶各等各的响应。</para>
/// </summary>
internal sealed class PermissionPromptBroker {
    private readonly Action<BridgeMessage> _send;
    private readonly TimeSpan _timeout;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<PermissionConfirmationDecision>> _pending = new(StringComparer.Ordinal);

    /// <summary>构造权限弹窗代理</summary>
    /// <param name="send">桥接消息出口（宿主注入 PostWebMessageAsJson 封装）</param>
    /// <param name="timeout">等待用户应答的上限（超时按 Deny；默认 5 分钟）</param>
    public PermissionPromptBroker(Action<BridgeMessage> send, TimeSpan? timeout = null) {
        ArgumentNullException.ThrowIfNull(send);
        _send = send;
        _timeout = timeout ?? TimeSpan.FromMinutes(5);
    }

    /// <summary>在途权限请求数（诊断/测试用）</summary>
    public int PendingCount => _pending.Count;

    /// <summary>
    /// 下发权限请求并等待用户决策；引擎已给出 RequestId 时沿用（便于日志串联），否则生成。
    /// 任何非成功路径（超时/取消）都返回 Deny，绝不抛出到引擎。
    /// </summary>
    public async Task<PermissionConfirmationDecision> RequestAsync(PermissionConfirmationRequest request, CancellationToken cancellationToken) {
        ArgumentNullException.ThrowIfNull(request);
        var id = string.IsNullOrWhiteSpace(request.RequestId) ? Guid.NewGuid().ToString("N") : request.RequestId;
        var completion = new TaskCompletionSource<PermissionConfirmationDecision>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion)) {
            // 同 Id 重复请求（引擎复用 RequestId）：拒绝新请求，避免覆盖在途应答
            App.LogDiag($"[WebUI] permission id 冲突 id={id} tool={request.ToolName}; fail-closed deny");
            return PermissionConfirmationDecision.Deny;
        }
        try {
            _send(new PermissionRequestMessage(
                id,
                request.ToolName,
                request.ConfirmationPrompt,
                request.RuleContent,
                request.DangerLevel?.ToValue()));
            return await completion.Task.WaitAsync(_timeout, cancellationToken);
        } catch (TimeoutException) {
            App.LogDiag($"[WebUI] permission timeout id={id} tool={request.ToolName}; fail-closed deny");
            return PermissionConfirmationDecision.Deny;
        } catch (OperationCanceledException) {
            App.LogDiag($"[WebUI] permission canceled id={id} tool={request.ToolName}; fail-closed deny");
            return PermissionConfirmationDecision.Deny;
        } finally {
            _pending.TryRemove(id, out _);
        }
    }

    /// <summary>
    /// 解析 web 回传的决策：命中在途请求则唤醒等待方；未知 Id 记诊断后忽略（不抛、不影响其它请求）。
    /// 决策值经 [EnumValue] 反查，非法值 fail-closed 按 Deny。
    /// </summary>
    public void Resolve(PermissionResponseMessage response) {
        ArgumentNullException.ThrowIfNull(response);
        var decision = ParseDecision(response.Decision);
        if (!_pending.TryRemove(response.Id, out var completion)) {
            App.LogDiag($"[WebUI] permission response 无匹配请求 id={response.Id} decision={response.Decision}");
            return;
        }
        App.LogDiag($"[WebUI] permission response id={response.Id} decision={decision.ToValue()}");
        completion.TrySetResult(decision);
    }

    /// <summary>窗口关闭/看门狗触发：全部在途请求按 Deny 收口（不得留下悬挂的引擎等待）</summary>
    public void CancelAll() {
        foreach (var pair in _pending) {
            if (_pending.TryRemove(pair.Key, out var completion)) {
                completion.TrySetResult(PermissionConfirmationDecision.Deny);
            }
        }
    }

    /// <summary>决策值反查（唯一数据源 = PermissionConfirmationDecision 的 [EnumValue]）；空/非法 → Deny</summary>
    internal static PermissionConfirmationDecision ParseDecision(string? value) =>
        PermissionConfirmationDecisionExtensions.FromValue(value) ?? PermissionConfirmationDecision.Deny;
}
