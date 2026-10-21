namespace NativeTui.Core.Bridge;

/// <summary>
/// bridge 事件 → REPL 视图状态应用器 — ADR 0141 事件语义的唯一翻译层：
/// 会话握手（session_info 更新 banner 真实数据 + footer 初始模式 + bypass 可用性）、
/// 权限模式回执（permission_mode_changed 更新 footer）、
/// 回合生命周期驱动 Busy + SpinnerItem（BeginTurn/EndTurn），消息事件插入滚动区
/// （spinner 恒为末项），token_delta 累加 ResponseLength，错误追加 ErrorMessageItem；
/// 工具调用聚合（M8-4）：连续同名可聚合调用（Read/Grep/Glob）合并为 CollapsedToolItem
/// （首次独立 ToolCallItem，第二次同名替换，后续 Record — 工具结果不断链，其他消息事件/回合边界断链）；
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

    /// <summary>活动流式正文项（M8-1 逐字流式）— 首个带 Text 的 token_delta 惰性创建落位 spinner 前，
    /// 后续增量原地追加；AssistantMessage 原位替换为完整消息项；EndTurn 兜底定格/清理。</summary>
    private StreamingTextItem? _streamingItem;

    /// <summary>活动工具调用聚合链（M8-4）— null = 无链（最近事件已断；详见 ToolCallChain）。</summary>
    private ToolCallChain? _activeToolChain;

    /// <summary>
    /// 工具调用聚合链（M8-4）— Driver 私有状态：连续同名可聚合调用（Read/Grep/Glob）的合并追踪。
    /// 链生命周期：首次调用独立上屏 ToolCallItem（链头候选）→ 第二次同名到达原位替换为 CollapsedToolItem
    /// （Count=2 起折叠形态）→ 后续同名 RecordCall 原地更新。断链条件：不同名/非聚合工具到达、
    /// 其他消息事件产生（流式正文/助手消息/错误/子代理组）、回合边界（BeginTurn/EndTurn）；
    /// 工具结果不断链（聚合仅统计 ToolCallItem — Read→Result→Read 仍聚合，结果照旧独立上屏）。
    /// </summary>
    private sealed class ToolCallChain(string toolName, string firstArgs, ToolCallItem head)
    {
        /// <summary>链工具名（同名合并键）。</summary>
        public string ToolName => toolName;

        /// <summary>链首次调用参数（替换链头时构造聚合项的历史首条）。</summary>
        public string FirstArgs => firstArgs;

        /// <summary>链头独立项（首次上屏的 ToolCallItem — 第二次同名到达时定位原位替换）。</summary>
        public ToolCallItem Head => head;

        /// <summary>激活的聚合项（null = 尚未聚合 — 链头仍为独立 ToolCallItem）。</summary>
        public CollapsedToolItem? Collapsed { get; set; }
    }

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

    /// <summary>登录门禁开（引擎 session_info 报告 LoggedIn — ApiKey 有效即能对话；false 时 shell 提交本地反馈不调引擎）。</summary>
    public bool IsLoggedIn { get; private set; } = true;

    /// <summary>应用一条事件（全量分派 — switch 模式匹配穷尽由编译器保证）。</summary>
    public void Apply(BridgeEvent bridgeEvent)
    {
        switch (bridgeEvent)
        {
            case BridgeEvent.SessionInfo(var version, var model, var permissionMode, var bypassAvailable, var cwd, var vendor, var loggedIn):
                ApplySessionInfo(version, model, permissionMode, bypassAvailable, cwd, vendor, loggedIn);
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
                ApplyToolCall(name, args);
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
            case BridgeEvent.TokenDelta(var length, var text):
                _spinner.Mode = SpinnerMode.Responding;
                _spinner.HasActiveTools = false;
                _spinner.ResponseLength += length;
                if (text is { Length: > 0 }) {
                    // M8-1 逐字流式 — 惰性建流式项（spinner 前）+ 原地追加（每帧全量重渲自然增长）
                    if (_streamingItem is null) {
                        EndToolChain();
                        _streamingItem = new StreamingTextItem();
                        AddMessage(_streamingItem);
                    }
                    _streamingItem.Append(text);
                }
                break;
            case BridgeEvent.AssistantMessage(var text, var markdown, var messageId, var thinkingDurationMs, var thinkingContent):
                EndToolChain();
                ReplaceStreamingWithFinal(markdown is not null
                    ? new MarkdownMessageItem(markdown, _codeHighlighter, thinkingDurationMs, thinkingContent, messageId)
                    : new AssistantMessageItem(text, thinkingDurationMs, thinkingContent, messageId));
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
            case BridgeEvent.SubagentFinished(var subagentId, var isError, var tokenTotal):
                _activeSubagentGroup?.FinishAgent(subagentId, isError, tokenTotal);
                if (_activeSubagentGroup is { AllComplete: true })
                {
                    _spinner.HasActiveTools = false;
                }

                break;
            case BridgeEvent.ContextUsage(_, _, var percentLeft):
                ApplyContextUsage(percentLeft);
                break;
            case BridgeEvent.Error(var message):
                EndToolChain();
                AddMessage(new ErrorMessageItem(message));
                break;
        }
    }

    /// <summary>子代理启动 — 惰性建组（组项落位 spinner 前）+ 条目追加 + spinner 切 ToolUse（子代理运行中主循环有活跃工具）。</summary>
    private void ApplySubagentStarted(string subagentId, string agentType, string description)
    {
        EndToolChain();
        if (_activeSubagentGroup is null)
        {
            _activeSubagentGroup = new SubagentGroupCoordinator();
            AddMessage(new SubagentGroupItem(_activeSubagentGroup));
        }

        _activeSubagentGroup.AddAgent(subagentId, agentType, description);
        _spinner.Mode = SpinnerMode.ToolUse;
        _spinner.HasActiveTools = true;
    }

    /// <summary>上下文余量路由（M8-7）— percentLeft 阈值形态写入 footer 告警槽
    /// （≤10% 告急 error / ≤25% 不足 warning / &gt;25% 清空防噪音；独立于 Notification 临时通知槽）。</summary>
    private void ApplyContextUsage(int percentLeft)
    {
        var (text, level) = FooterState.ResolveContextWarning(percentLeft);
        _view.Footer = _view.Footer with { ContextWarning = text, ContextWarningLevel = level };
    }

    /// <summary>ToolCall 路由（M8-4 聚合）— 同名可聚合链上合并（已聚合 Record / 未聚合替换链头），
    /// 不同名或非聚合工具断链后独立上屏（单次调用保持 ToolCallItem 类型 — 视觉与断言面零变化）。</summary>
    private void ApplyToolCall(string name, string args)
    {
        if (_activeToolChain is { } chain && chain.ToolName == name)
        {
            ExtendChain(chain, args);
            return;
        }

        EndToolChain();
        if (CollapsedToolItem.IsAggregatable(name))
        {
            var head = new ToolCallItem(name, args);
            AddMessage(head);
            _activeToolChain = new ToolCallChain(name, args, head);
            return;
        }

        AddMessage(new ToolCallItem(name, args));
    }

    /// <summary>链上后续同名调用 — 已聚合则原地 Record（计数与最新提示行更新），
    /// 未聚合则原位替换链头为聚合项（Count=2 起折叠形态 — 位置不变视觉无跳变）。</summary>
    private void ExtendChain(ToolCallChain chain, string args)
    {
        if (chain.Collapsed is { } collapsed)
        {
            collapsed.RecordCall(args);
            return;
        }

        var merged = new CollapsedToolItem(chain.ToolName, [chain.FirstArgs, args]);
        ReplaceItem(chain.Head, merged);
        chain.Collapsed = merged;
    }

    /// <summary>断链（幂等）— 不同名工具/其他消息事件/回合边界调用。</summary>
    private void EndToolChain() => _activeToolChain = null;

    /// <summary>消息项原位替换（聚合链头 → 聚合项；位置不变视觉无跳变，未找到时退化为 AddMessage）。</summary>
    private void ReplaceItem(IScrollItem oldItem, IScrollItem newItem)
    {
        var index = _view.Messages.IndexOf(oldItem);
        if (index < 0)
        {
            AddMessage(newItem);
            return;
        }

        _view.Messages.RemoveAt(index);
        _view.Messages.Insert(index, newItem);
    }

    /// <summary>应用会话握手：banner 换引擎真实数据（模型 null → "当前模型未设置"；供应商 null → 空，banner 退化无前缀）
    /// + footer 初始模式 + bypass 可用性 + 登录门禁态（footer 未登录提示真实化 — TASK060）。</summary>
    private void ApplySessionInfo(string version, string? model, string permissionMode, bool bypassAvailable, string cwd, string? vendor, bool loggedIn)
    {
        _banner.Version = version;
        _banner.Model = string.IsNullOrWhiteSpace(model) ? "当前模型未设置" : model;
        _banner.Vendor = vendor ?? string.Empty;
        _banner.Cwd = cwd;
        BypassAvailable = bypassAvailable;
        IsLoggedIn = loggedIn;
        _view.Footer = _view.Footer with { LoginError = loggedIn ? null : "未登录 · 运行 /login" };
        if (PermissionModeInfo.TryFromValue(permissionMode, out var parsed))
        {
            _view.Footer = _view.Footer with { Mode = parsed };
        }
    }

    /// <summary>开始回合：busy + spinner 挂载（Thinking 乐观模式，动词重采样，停滞计时重置）；防御性脱离子代理组与流式项（引擎漏发回合收尾时不串组/不残留）。
    /// spinner 显示（对齐基准 REPL.tsx showSpinner：isLoading 期等待/思考/工具运行的反馈 — 流式正文到达前 M8-1 前为唯一反馈）。</summary>
    public void BeginTurn()
    {
        IsBusy = true;
        _activeSubagentGroup = null;
        EndToolChain();
        DiscardStreamingItem();
        _view.Footer = _view.Footer with { Busy = true };
        _spinner.Message = string.Empty;
        _spinner.ResetVerb();
        _spinner.Mode = SpinnerMode.Thinking;
        _spinner.LoadingStartMs = Environment.TickCount64;
        _spinner.ResponseLength = 0;
        _spinner.HasActiveTools = false;
        _spinner.Hidden = false;
    }

    /// <summary>结束回合：idle + spinner 复位（消息由事件驱动，interrupted 不额外追加）；子代理组定格脱离（未完成条目定格为非错误完成）。
    /// 流式项兜底（M8-1）：引擎被打断/异常未发 assistant_message 时 — 非空定格为 partial AssistantMessage
    /// （基准 [user, partial, interrupted] 顺序 — 已流文本不丢），空则移除（无正文零残留）。
    /// spinner Mode 复位 Thinking→Responding（修复：回合结束后思考动画续转 — Thinking 态渲染器持续 shimmer）；
    /// Hidden 置位（对齐基准：空闲非 busy 完全不渲染 spinner 行 — 不再常驻闪烁）。</summary>
    public void EndTurn()
    {
        IsBusy = false;
        _activeSubagentGroup?.FinalizePending();
        _activeSubagentGroup = null;
        EndToolChain();
        FinalizeStreamingItem();
        _view.Footer = _view.Footer with { Busy = false };
        _spinner.Message = string.Empty;
        _spinner.ResponseLength = 0;
        _spinner.HasActiveTools = false;
        _spinner.Hidden = true;
        if (_spinner.Mode == SpinnerMode.Thinking)
        {
            _spinner.Mode = SpinnerMode.Responding;
        }
    }

    /// <summary>消息插入 spinner 之前（spinner 恒为滚动区最后一项）。</summary>
    public void AddMessage(IScrollItem item) =>
        _view.Messages.Insert(_view.Messages.Count - 1, item);

    /// <summary>流式项原位替换为完整消息项（OnDone 路径）— 流式项位置不变（视觉无跳变），
    /// 无流式项时退化为普通 AddMessage。</summary>
    private void ReplaceStreamingWithFinal(IScrollItem final)
    {
        if (_streamingItem is null) {
            AddMessage(final);
            return;
        }

        var index = _view.Messages.IndexOf(_streamingItem);
        _streamingItem = null;
        if (index < 0) {
            AddMessage(final);
            return;
        }

        _view.Messages.RemoveAt(index);
        _view.Messages.Insert(index, final);
    }

    /// <summary>EndTurn 兜底 — 残留流式项定格（非空 → partial AssistantMessage）或移除（空 — 无正文零残留）。</summary>
    private void FinalizeStreamingItem()
    {
        if (_streamingItem is null) {
            return;
        }

        if (_streamingItem.Text.Length == 0) {
            DiscardStreamingItem();
            return;
        }

        ReplaceStreamingWithFinal(new AssistantMessageItem(_streamingItem.Text));
    }

    /// <summary>BeginTurn 防御 — 清除上一回合残留流式项（引擎漏发收尾事件时不跨回合泄漏）。</summary>
    private void DiscardStreamingItem()
    {
        if (_streamingItem is null) {
            return;
        }

        var index = _view.Messages.IndexOf(_streamingItem);
        _streamingItem = null;
        if (index >= 0) {
            _view.Messages.RemoveAt(index);
        }
    }
}
