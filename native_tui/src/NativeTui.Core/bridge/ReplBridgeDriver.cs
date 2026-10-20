namespace NativeTui.Core.Bridge;

/// <summary>
/// bridge 事件 → REPL 视图状态应用器 — ADR 0141 事件语义的唯一翻译层：
/// 会话握手（session_info 更新 banner 真实数据 + footer 初始模式 + bypass 可用性）、
/// 权限模式回执（permission_mode_changed 更新 footer）、
/// 回合生命周期驱动 Busy + SpinnerItem（BeginTurn/EndTurn），消息事件插入滚动区
/// （spinner 恒为末项），token_delta 累加 ResponseLength，错误追加 ErrorMessageItem；
/// 权限请求（permission_request）经注入钩子转交 shell 挂起对话框（权限模态是 shell 层状态）。
/// 模拟路径与真实 bridge 路径共用同一套回合视图状态。
/// </summary>
public sealed class ReplBridgeDriver
{
    private readonly ReplView _view;
    private readonly SpinnerItem _spinner;
    private readonly BannerItem _banner;

    /// <summary>权限请求挂起钩子（shell 注入 BeginPermissionRequest — 权限模态是 shell 层状态，Driver 只路由不持有）。</summary>
    private readonly Action<string, string>? _onPermissionRequest;

    /// <summary>代码高亮器（宿主注入的卫星插件 — null = 高亮禁用降级纯文本，MarkdownMessageItem 消费）。</summary>
    private readonly ICodeHighlighter? _codeHighlighter;

    /// <summary>活动子代理组（M7-7）— 回合内惰性建组，TurnCompleted/Interrupted 定格脱离；历史组留滚动区。</summary>
    private SubagentGroupCoordinator? _activeSubagentGroup;

    /// <summary>创建应用器（view/spinner/banner 由调用方构造并挂载，spinner 须为 Messages 末项、banner 为首项；
    /// onPermissionRequest = 权限请求挂起钩子，null = 忽略权限请求事件（纯视图断言测试场景）；
    /// codeHighlighter = 代码高亮插件（M7-5），null = 高亮禁用）。</summary>
    public ReplBridgeDriver(
        ReplView view,
        SpinnerItem spinner,
        BannerItem banner,
        Action<string, string>? onPermissionRequest = null,
        ICodeHighlighter? codeHighlighter = null)
    {
        _view = view;
        _spinner = spinner;
        _banner = banner;
        _onPermissionRequest = onPermissionRequest;
        _codeHighlighter = codeHighlighter;
    }

    /// <summary>回合进行中（turn_started 后未 completed/interrupted）。</summary>
    public bool IsBusy { get; private set; }

    /// <summary>bypass 模式是否可用（引擎 session_info 报告 — settings.json 安全闸禁用时为 false，Cycler 跳过 bypass）。</summary>
    public bool BypassAvailable { get; private set; } = true;

    /// <summary>应用一条事件（全量分派 — switch 模式匹配穷尽由编译器保证）。</summary>
    public void Apply(BridgeEvent bridgeEvent)
    {
        switch (bridgeEvent)
        {
            case BridgeEvent.SessionInfo(var version, var model, var permissionMode, var bypassAvailable, var cwd):
                ApplySessionInfo(version, model, permissionMode, bypassAvailable, cwd);
                break;
            case BridgeEvent.PermissionModeChanged(var mode):
                if (PermissionModeInfo.TryFromValue(mode, out var parsed))
                {
                    _view.Footer = _view.Footer with { Mode = parsed };
                }

                break;
            case BridgeEvent.TurnStarted:
                BeginTurn();
                break;
            case BridgeEvent.ThinkingStarted:
                _spinner.Mode = SpinnerMode.Thinking;
                _spinner.ResetVerb();
                break;
            case BridgeEvent.ThinkingEnded:
                // 思考时长由 ThinkingLifecycle 内部按墙钟计时（M6d）；
                // durationMs 供引擎侧比对，视图侧模式切换由后续事件（tool_call/token_delta）驱动。
                break;
            case BridgeEvent.ToolCall(var name, var args):
                AddMessage(new ToolCallItem(name, args));
                _spinner.Mode = SpinnerMode.ToolUse;
                _spinner.HasActiveTools = true;
                break;
            case BridgeEvent.ToolResult(var summary, var patch):
                AddMessage(patch is { Length: > 0 }
                    ? new DiffResultItem(summary, patch)
                    : new ToolResultItem(summary));
                _spinner.HasActiveTools = false;
                break;
            case BridgeEvent.PermissionRequest(var toolName, var description):
                if (_onPermissionRequest is { } beginPermission)
                {
                    beginPermission(toolName, description);
                }

                break;
            case BridgeEvent.TokenDelta(var length):
                _spinner.Mode = SpinnerMode.Responding;
                _spinner.HasActiveTools = false;
                _spinner.ResponseLength += length;
                break;
            case BridgeEvent.AssistantMessage(var text, var markdown):
                AddMessage(markdown is not null
                    ? new MarkdownMessageItem(markdown, _codeHighlighter)
                    : new AssistantMessageItem(text));
                break;
            case BridgeEvent.TurnCompleted:
                EndTurn();
                break;
            case BridgeEvent.TurnInterrupted:
                EndTurn();
                break;
            case BridgeEvent.SubagentStarted(var subagentId, var agentType, var description):
                ApplySubagentStarted(subagentId, agentType, description);
                break;
            case BridgeEvent.SubagentToolUse(var subagentId, var toolName, var summary, var tokenGain):
                _activeSubagentGroup?.RecordToolUse(subagentId, toolName, summary, tokenGain);
                break;
            case BridgeEvent.SubagentFinished(var subagentId, var isError):
                _activeSubagentGroup?.FinishAgent(subagentId, isError);
                if (_activeSubagentGroup is { AllComplete: true })
                {
                    _spinner.HasActiveTools = false;
                }

                break;
            case BridgeEvent.Error(var message):
                AddMessage(new ErrorMessageItem(message));
                break;
        }
    }

    /// <summary>子代理启动 — 惰性建组（组项落位 spinner 前）+ 条目追加 + spinner 切 ToolUse（子代理运行中主循环有活跃工具）。</summary>
    private void ApplySubagentStarted(string subagentId, string agentType, string description)
    {
        if (_activeSubagentGroup is null)
        {
            _activeSubagentGroup = new SubagentGroupCoordinator();
            AddMessage(new SubagentGroupItem(_activeSubagentGroup));
        }

        _activeSubagentGroup.AddAgent(subagentId, agentType, description);
        _spinner.Mode = SpinnerMode.ToolUse;
        _spinner.HasActiveTools = true;
    }

    /// <summary>应用会话握手：banner 换引擎真实数据（模型 null → "当前模型未设置"）+ footer 初始模式 + bypass 可用性。</summary>
    private void ApplySessionInfo(string version, string? model, string permissionMode, bool bypassAvailable, string cwd)
    {
        _banner.Version = version;
        _banner.Model = string.IsNullOrWhiteSpace(model) ? "当前模型未设置" : model;
        _banner.Cwd = cwd;
        BypassAvailable = bypassAvailable;
        if (PermissionModeInfo.TryFromValue(permissionMode, out var parsed))
        {
            _view.Footer = _view.Footer with { Mode = parsed };
        }
    }

    /// <summary>开始回合：busy + spinner 挂载（Thinking 乐观模式，动词重采样，停滞计时重置）；防御性脱离子代理组（引擎漏发回合收尾时不串组）。</summary>
    public void BeginTurn()
    {
        IsBusy = true;
        _activeSubagentGroup = null;
        _view.Footer = _view.Footer with { Busy = true };
        _spinner.Message = string.Empty;
        _spinner.ResetVerb();
        _spinner.Mode = SpinnerMode.Thinking;
        _spinner.LoadingStartMs = Environment.TickCount64;
        _spinner.ResponseLength = 0;
        _spinner.HasActiveTools = false;
    }

    /// <summary>结束回合：idle + spinner 复位（消息由事件驱动，interrupted 不额外追加）；子代理组定格脱离（未完成条目定格为非错误完成）。</summary>
    public void EndTurn()
    {
        IsBusy = false;
        _activeSubagentGroup?.FinalizePending();
        _activeSubagentGroup = null;
        _view.Footer = _view.Footer with { Busy = false };
        _spinner.Message = string.Empty;
        _spinner.ResponseLength = 0;
        _spinner.HasActiveTools = false;
    }

    /// <summary>消息插入 spinner 之前（spinner 恒为滚动区最后一项）。</summary>
    public void AddMessage(IScrollItem item) =>
        _view.Messages.Insert(_view.Messages.Count - 1, item);
}
