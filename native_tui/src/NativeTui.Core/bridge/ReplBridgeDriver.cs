namespace NativeTui.Core.Bridge;

/// <summary>
/// bridge 事件 → REPL 视图状态应用器 — ADR 0141 事件语义的唯一翻译层：
/// 会话握手（session_info 更新 banner 真实数据 + footer 初始模式 + bypass 可用性）、
/// 权限模式回执（permission_mode_changed 更新 footer）、
/// 回合生命周期驱动 Busy + SpinnerItem（BeginTurn/EndTurn），消息事件插入滚动区
/// （spinner 恒为末项），token_delta 累加 ResponseLength，token_usage 转交 TokenUsageTracker
/// 记账（footer 用量展示归 tracker，Driver 只路由不持有 UI 态），错误追加 ErrorMessageItem；
/// 工具调用聚合路由（S6 任务 7）：连续同类 Read/Grep/Glob 聚合为 CollapsedToolGroupItem
/// 单行摘要（组状态归 ToolGroupAggregator），非 Accepts 工具/其他消息事件/回合边界收组定格；
/// 小终端塌缩（ShouldCondense）时 CondensedToolProgressItem 单行替代逐条。
/// 权限请求（permission_request）经注入钩子转交 shell 挂起对话框（权限模态是 shell 层状态）；
/// 后台任务（background_task）路由常驻面板 upsert（M8-8 — (kind,taskId) 更新/插入，终态驻留）；
/// 工具进度（tool_progress）路由流式输出项原地更新（TASK065 P3 — Bash 执行中输出实时显示，
/// tool_result 到达原位替换为 ToolResultItem）。
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

    /// <summary>流式 Markdown 投影器（方案 B 卫星注入 — null = 流式项降级纯文本逐字 M8-1 行为）。</summary>
    private readonly IStreamingMarkdownProjector? _streamingProjector;
        /// <summary>issue 引用短形态上下文（DSG043 尾批 — "owner/repo"，git remote 解析注入；
    /// null = 短形态 #NNN 不展开，完整形态 owner/repo#NNN 恒展开）。</summary>
    private readonly string? _issueOwnerRepo;

    /// <summary>令牌用量记账器（S4 契约 — 引擎 token_usage 事件转交 RecordUsage；null = 未注入不记账，
    /// 事件静默容忍（纯视图断言测试场景），footer 用量展示归 tracker 内部驱动）。</summary>
    private readonly TokenUsageTracker? _tokenUsageTracker;

    /// <summary>终端尺寸源（S6 任务 7 塌缩路由 — ShouldCondense 的 rows 输入；null = 不塌缩恒逐条/聚合路由）。</summary>
    private readonly ITerminalSizeSource? _sizeSource;

    /// <summary>活动工具聚合组（S6 任务 7 — 连续同类 Read/Grep/Glob 聚合；非 Accepts 工具或回合边界收组定格）。</summary>
    private ToolGroupAggregator? _activeToolGroup;

    /// <summary>活动聚合组渲染项（spinner 前落位；收组时 IsActive=false 定格为普通项）。</summary>
    private CollapsedToolGroupItem? _activeGroupItem;

    /// <summary>小终端塌缩进度项（ShouldCondense 判定命中时启用 — 逐条/聚合渲染的单行替代形态；
    /// 活动工具计数归零时移除；反向切换（塌缩恢复逐条）本回合不做防边界抖动）。</summary>
    private CondensedToolProgressItem? _condensedProgressItem;

    /// <summary>进行中工具调用计数（ToolCall +1 / ToolResult -1 — 塌缩判定与塌缩单行计数数据源）。</summary>
    private int _inProgressToolCallCount;

    /// <summary>活动子代理组（M7-7）— 回合内惰性建组，TurnCompleted/Interrupted 定格脱离；历史组留滚动区。</summary>
    private SubagentGroupCoordinator? _activeSubagentGroup;

    /// <summary>活动流式正文项（M8-1 逐字流式）— 首个带 Text 的 token_delta 惰性创建落位 spinner 前，
    /// 后续增量原地追加；AssistantMessage 原位替换为完整消息项；EndTurn 兜底定格/清理。</summary>
    private StreamingTextItem? _streamingItem;

    /// <summary>活动工具流式输出项（TASK065 P3 — Bash 执行中输出实时显示）— 首个 tool_progress
    /// 惰性创建落位 ToolCallItem 之后（spinner 前），后续快照原地 Update；tool_result 到达原位替换为
    /// ToolResultItem（对齐 StreamingTextItem 替换先例）；新 tool_call 到达旧项定格；EndTurn 清引用
    /// 项留存（打断场景已见输出不丢）。</summary>
    private ToolStreamingItem? _toolStreamingItem;

    /// <summary>进行中工具信息文本（TASK066 F4 — spinner lastToolInfo 数据源）：
    /// ToolCall 时记录（所有路径统一 — 逐条/聚合/塌缩均覆盖），ToolResult 到达消费转
    /// <c>_spinner.LastToolInfo</c>（基准 AgentTool/UI.tsx:788 extractLastToolInfo 的
    /// "最近 tool_result 对应 tool_use" 语义 — 工具串行一进一出配对）；
    /// TurnInterrupted 时未消费 → 追加中断标记；EndTurn 清引用不跨回合泄漏。</summary>
    private string? _pendingToolInfo;

    /// <summary>创建应用器（view/spinner/banner 由调用方构造并挂载，spinner 须为 Messages 末项、banner 为首项；
    /// onPermissionRequest = 权限请求挂起钩子，null = 忽略权限请求事件（纯视图断言测试场景）；
    /// codeHighlighter = 代码高亮插件（M7-5），null = 高亮禁用；
    /// tokenUsageTracker = 令牌用量记账器（S4 契约，token_usage 事件转交），null = 不记账；
    /// sizeSource = 终端尺寸源（S6 任务 7 塌缩路由），null = 不塌缩；
    /// streamingProjector = 流式 Markdown 块级增量投影器（方案 B 卫星注入），null = 纯文本降级。</summary>
    public ReplBridgeDriver(
        ReplView view,
        SpinnerItem spinner,
        BannerItem banner,
        Action<string, string>? onPermissionRequest = null,
        ICodeHighlighter? codeHighlighter = null,
        TokenUsageTracker? tokenUsageTracker = null,
        ITerminalSizeSource? sizeSource = null,
        IStreamingMarkdownProjector? streamingProjector = null,
        string? issueOwnerRepo = null)
    {
        _view = view;
        _spinner = spinner;
        _banner = banner;
        _onPermissionRequest = onPermissionRequest;
        _codeHighlighter = codeHighlighter;
        _tokenUsageTracker = tokenUsageTracker;
        _sizeSource = sizeSource;
        _streamingProjector = streamingProjector;
        _issueOwnerRepo = issueOwnerRepo;
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
            case BridgeEvent.ToolProgress(_, _, var progressMessage):
                // TASK065 P3 — Bash/PowerShell 执行中输出快照流式上屏（progressType/工具名不路由 —
                // 流式项即输出尾巴，类型差异由终态 ToolResultItem 表达）
                ApplyToolProgress(progressMessage);
                break;
            case BridgeEvent.ToolResult(var summary, var patch, var rawOutput, var hiddenLines):
                // S2 契约统一结果项：ToolResultItem(Summary, Patch, RawOutput, HiddenLines) —
                // patch/rawOutput/hiddenLines 全量透传（折叠/展开渲染归 item，Driver 只路由不解释）
                ApplyToolResult(summary, patch, rawOutput, hiddenLines);
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
                    // M8-1 逐字流式 — 惰性建流式项（spinner 前）+ 原地追加（每帧全量重渲自然增长）；
                    // 方案 B — 注入投影器则流式期 Markdown 实时渲染（块级增量），null 降级纯文本
                    if (_streamingItem is null) {
                        CloseActiveToolGroup();
                        _streamingItem = new StreamingTextItem(_streamingProjector);
                        AddMessage(_streamingItem);
                    }
                    _streamingItem.Append(text);
                }
                break;
            case BridgeEvent.AssistantMessage(var text, var markdown, var messageId, var thinkingDurationMs, var thinkingContent):
                CloseActiveToolGroup();
                ReplaceStreamingWithFinal(markdown is not null
                    ? new MarkdownMessageItem(IssueLinkExpander.ExpandDocument(markdown, _issueOwnerRepo), _codeHighlighter, thinkingDurationMs, thinkingContent, messageId)
                    : new AssistantMessageItem(text, thinkingDurationMs, thinkingContent, messageId));
                break;
            case BridgeEvent.TokenUsage(var inputTokens, var outputTokens, var contextTokens, var contextLimitTokens):
                // S4 契约 — 用量记账归 tracker（footer 展示由 tracker 内部驱动）；未注入时静默容忍
                _tokenUsageTracker?.RecordUsage(inputTokens, outputTokens, contextTokens, contextLimitTokens);
                break;
            case BridgeEvent.TurnCompleted:
                EndTurn();
                break;
            case BridgeEvent.TurnInterrupted:
                // TASK066 F4 — 中断路径带标记：进行中工具（pending 未消费）追加"（已中断）"状态标记
                EndTurn(interrupted: true);
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
            case BridgeEvent.BackgroundTask(var kind, var taskId, var description, var state):
                // M8-8 常驻面板 upsert — (kind,taskId) 更新/插入条目，终态驻留不随回合清理
                _view.BackgroundTasks.Upsert(kind, taskId, description, state);
                break;
            case BridgeEvent.Error(var message):
                CloseActiveToolGroup();
                AddMessage(new ErrorMessageItem(message));
                break;
        }
    }

    /// <summary>工具调用路由（S6 任务 7 — 聚合/塌缩/逐条三分派）：
    /// ① 塌缩态已建立 → 保持塌缩（计数原地更新，防边界抖动 — 本回合不反向切换）；
    /// ② 进行中计数 +1 后满足 ShouldCondense（小终端）→ 塌缩单行项替代逐条落位；
    /// ③ 连续同类（Accepts：Read/Grep/Glob）→ 活动聚合组吸收（不落逐条 ToolCallItem，
    /// 首条惰性建组 — 组项落位 spinner 前）；
    /// ④ 非 Accepts 工具 → 收组（连续同类中断 — 聚合项定格为普通项）+ 逐条落位。
    /// 入口统一记录 pending 工具信息（TASK066 F4 — 逐条/聚合/塌缩全路径覆盖，
    /// ToolCallItem 生成点的等价超集）。</summary>
    private void ApplyToolCall(string name, string args)
    {
        // 上一个工具的流式尾巴定格（引用清空不删项 — 引擎漏发 tool_result 时不串新工具输出）
        _toolStreamingItem = null;
        _pendingToolInfo = ToolTitleFormatter.FormatToolInfo(name, args);
        _inProgressToolCallCount++;
        if (_condensedProgressItem is not null)
        {
            _condensedProgressItem.ToolUses = _inProgressToolCallCount;
            TrackToolGroup(name, args);
            return;
        }

        if (ShouldCondenseNow())
        {
            // 组项退场（塌缩单行是唯一进度展示形态 — 组状态保留继续吸收计数，渲染项移除）
            if (_activeGroupItem is { } groupItem)
            {
                RemoveMessageItem(groupItem);
                _activeGroupItem = null;
            }

            _condensedProgressItem = new CondensedToolProgressItem(_inProgressToolCallCount);
            AddMessage(_condensedProgressItem);
            TrackToolGroup(name, args);
            return;
        }

        if (TrackToolGroup(name, args))
        {
            return;
        }

        AddMessage(new ToolCallItem(name, args));
    }

    /// <summary>工具结果路由（S6 任务 7）— 计数递减；塌缩态计数归零移除塌缩项；
    /// 活动聚合组吸收（不落逐条 ToolResultItem — 折叠形态结果数据不逐条上屏）；
    /// 其余逐条落位（统一四段契约构造）；流式项在场时原位替换（TASK065 P3 —
    /// 输出尾巴位置落终态结果项，视觉无跳变；塌缩/聚合吸收路径移除流式项）。
    /// pending 工具信息消费（TASK066 F4 — 塌缩/聚合/逐条全路径前置：最近完成的工具
    /// 转 <c>_spinner.LastToolInfo</c>，异常序（无 pending）保持原值不动）。</summary>
    private void ApplyToolResult(string summary, DiffHunk[]? patch, string? rawOutput, int hiddenLines)
    {
        _inProgressToolCallCount = Math.Max(0, _inProgressToolCallCount - 1);
        if (_pendingToolInfo is { } completed)
        {
            _spinner.LastToolInfo = completed;
            _pendingToolInfo = null;
        }

        if (_condensedProgressItem is { } condensed)
        {
            condensed.ToolUses = _inProgressToolCallCount;
            if (_inProgressToolCallCount == 0)
            {
                RemoveMessageItem(condensed);
                _condensedProgressItem = null;
            }

            RemoveToolStreamingItem();
            _activeToolGroup?.OnToolResult();
            return;
        }

        if (_activeToolGroup is { } group)
        {
            RemoveToolStreamingItem();
            group.OnToolResult();
            return;
        }

        ReplaceToolStreamingWithResult(new ToolResultItem(summary, patch, rawOutput, hiddenLines, codeHighlighter: _codeHighlighter));
    }

    /// <summary>工具进度路由（TASK065 P3）— 回合内惰性建流式项（spinner 前落位 — 紧随 ToolCallItem）
    /// + 快照原地 Update；塌缩态忽略（CondensedToolProgressItem 是唯一进度展示形态 — 既有语义）；
    /// 回合外迟到事件丢弃（IsBusy 防御 — 引擎漏发收尾时不跨回合泄漏）。progressType 不路由 —
    /// 全类型统一输出尾巴形态（bash_progress/ps_progress/query_update 等都受益）。</summary>
    private void ApplyToolProgress(string message)
    {
        if (!IsBusy || _condensedProgressItem is not null)
        {
            return;
        }

        if (_toolStreamingItem is null)
        {
            _toolStreamingItem = new ToolStreamingItem();
            AddMessage(_toolStreamingItem);
        }

        _toolStreamingItem.Update(message);
    }

    /// <summary>聚合组路由 — 非 Accepts 工具到达时收组（连续同类中断）；Accepts 且无活动组时
    /// 惰性建组（aggregator + 组项 spinner 前落位）；组内 OnToolCall 计数/hint。返回 true = 组吸收。</summary>
    private bool TrackToolGroup(string name, string args)
    {
        if (_activeToolGroup is not null && !ToolGroupAggregator.Accepts(name))
        {
            CloseActiveToolGroup();
        }

        if (_activeToolGroup is null && ToolGroupAggregator.Accepts(name))
        {
            _activeToolGroup = new ToolGroupAggregator();
            _activeGroupItem = new CollapsedToolGroupItem(_activeToolGroup);
            AddMessage(_activeGroupItem);
        }

        if (_activeToolGroup is { } group)
        {
            group.OnToolCall(name, args);
            return true;
        }

        return false;
    }

    /// <summary>收组 — 聚合项定格为普通项（IsActive=false，dim 渲染）；组引用清空（下一段同类序列开新组）。</summary>
    private void CloseActiveToolGroup()
    {
        if (_activeGroupItem is not null)
        {
            _activeGroupItem.IsActive = false;
        }

        _activeToolGroup = null;
        _activeGroupItem = null;
    }

    /// <summary>小终端塌缩判定 — 进行中计数纳入本条后 ShouldCondense（AgentTool/UI.tsx L469-470）；
    /// 未注入尺寸源恒 false（不塌缩）。</summary>
    private bool ShouldCondenseNow() =>
        _sizeSource is { } size
        && ToolFoldingPolicy.ShouldCondense(size.Current.Rows, _inProgressToolCallCount);

    /// <summary>移除滚动区指定项（找不到 no-op — 防御消息动态增删）。</summary>
    private void RemoveMessageItem(IScrollItem item)
    {
        var index = _view.Messages.IndexOf(item);
        if (index >= 0)
        {
            _view.Messages.RemoveAt(index);
        }
    }

    /// <summary>子代理启动 — 惰性建组（组项落位 spinner 前）+ 条目追加 + spinner 切 ToolUse（子代理运行中主循环有活跃工具）。</summary>
    private void ApplySubagentStarted(string subagentId, string agentType, string description)
    {
        CloseActiveToolGroup();
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
    /// spinner 显示（对齐基准 REPL.tsx showSpinner：isLoading 期等待/思考/工具运行的反馈 — 流式正文到达前 M8-1 前为唯一反馈）。
    /// lastToolInfo/pending 清零（TASK066 F4 — 新回合重新记录，上回合工具信息不跨回合残留）。</summary>
    public void BeginTurn()
    {
        IsBusy = true;
        _activeSubagentGroup = null;
        CloseActiveToolGroup();
        DiscardStreamingItem();
        _toolStreamingItem = null;
        _pendingToolInfo = null;
        _view.Footer = _view.Footer with { Busy = true };
        _spinner.Message = string.Empty;
        _spinner.ResetVerb();
        _spinner.Mode = SpinnerMode.Thinking;
        _spinner.LoadingStartMs = Environment.TickCount64;
        _spinner.ResponseLength = 0;
        _spinner.HasActiveTools = false;
        _spinner.Hidden = false;
        _spinner.LastToolInfo = null;
    }

    /// <summary>结束回合：idle + spinner 复位（消息由事件驱动，interrupted 不额外追加）；子代理组定格脱离（未完成条目定格为非错误完成）；
    /// 工具聚合组收组定格（S6 任务 7 回合边界 — 聚合项 IsActive=false 转普通项）。
    /// 流式项兜底（M8-1）：引擎被打断/异常未发 assistant_message 时 — 非空定格为 partial AssistantMessage
    /// （基准 [user, partial, interrupted] 顺序 — 已流文本不丢），空则移除（无正文零残留）。
    /// spinner Mode 复位 Thinking→Responding（修复：回合结束后思考动画续转 — Thinking 态渲染器持续 shimmer）；
    /// Hidden 置位（对齐基准：空闲非 busy 完全不渲染 spinner 行 — 不再常驻闪烁）。
    /// 中断标记（TASK066 F4）：interrupted 且 pending 未消费（工具进行中被打断）→
    /// <c>_spinner.LastToolInfo</c> 追加"（已中断）"标记后定格；正常完成漏发 result 不标记
    /// （保持最近完成态 — 引擎侧收尾事件的宽容语义）。</summary>
    public void EndTurn(bool interrupted = false)
    {
        IsBusy = false;
        _activeSubagentGroup?.FinalizePending();
        _activeSubagentGroup = null;
        CloseActiveToolGroup();
        FinalizeStreamingItem();
        _toolStreamingItem = null;
        if (interrupted && _pendingToolInfo is { } pending)
        {
            _spinner.LastToolInfo = $"{pending}（已中断）";
        }

        _pendingToolInfo = null;
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

    /// <summary>流式项原位替换为终态结果项（tool_result 路径 — TASK065 P3）— 流式项位置不变
    ///（输出尾巴位置直接落 ToolResultItem，视觉无跳变），无流式项时退化为普通 AddMessage
    ///（对齐 ReplaceStreamingWithFinal 先例）。</summary>
    private void ReplaceToolStreamingWithResult(IScrollItem final)
    {
        if (_toolStreamingItem is null) {
            AddMessage(final);
            return;
        }

        var index = _view.Messages.IndexOf(_toolStreamingItem);
        _toolStreamingItem = null;
        if (index < 0) {
            AddMessage(final);
            return;
        }

        _view.Messages.RemoveAt(index);
        _view.Messages.Insert(index, final);
    }

    /// <summary>移除活动工具流式项（塌缩/聚合吸收路径 — 工具完成时尾巴退场；不在场 no-op）。</summary>
    private void RemoveToolStreamingItem()
    {
        if (_toolStreamingItem is null) {
            return;
        }

        RemoveMessageItem(_toolStreamingItem);
        _toolStreamingItem = null;
    }
}
