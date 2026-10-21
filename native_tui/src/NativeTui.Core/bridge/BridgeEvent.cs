namespace NativeTui.Core.Bridge;

/// <summary>
/// bridge 事件（Engine → TUI，读子进程 stdout）— ADR 0141 事件契约：
/// 会话握手（session_info）、权限模式（permission_mode_changed）、
/// 回合生命周期（turn_started/completed/interrupted）、思考（thinking_started/ended）、
/// 工具（tool_call/tool_result/permission_request）、流式（token_delta）、消息（assistant_message/error）。
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SessionInfo), "session_info")]
[JsonDerivedType(typeof(PermissionModeChanged), "permission_mode_changed")]
[JsonDerivedType(typeof(TurnStarted), "turn_started")]
[JsonDerivedType(typeof(ThinkingStarted), "thinking_started")]
[JsonDerivedType(typeof(ThinkingEnded), "thinking_ended")]
[JsonDerivedType(typeof(ToolCall), "tool_call")]
[JsonDerivedType(typeof(ToolResult), "tool_result")]
[JsonDerivedType(typeof(PermissionRequest), "permission_request")]
[JsonDerivedType(typeof(TokenDelta), "token_delta")]
[JsonDerivedType(typeof(AssistantMessage), "assistant_message")]
[JsonDerivedType(typeof(TurnCompleted), "turn_completed")]
[JsonDerivedType(typeof(TurnInterrupted), "turn_interrupted")]
[JsonDerivedType(typeof(SubagentStarted), "subagent_started")]
[JsonDerivedType(typeof(SubagentToolUse), "subagent_tool_use")]
[JsonDerivedType(typeof(SubagentFinished), "subagent_finished")]
[JsonDerivedType(typeof(Error), "error")]
public abstract record BridgeEvent
{
    /// <summary>
    /// 会话握手（引擎启动后首条事件）— 版本/当前模型/权限模式/bypass 可用性/cwd/供应商/登录态。
    /// Model 为 null 表示引擎未设置模型（banner 显示"当前模型未设置"）；
    /// Vendor 为 null 表示引擎未上报（banner 退化为不带供应商前缀，旧引擎兼容）；
    /// LoggedIn 默认 true（旧引擎未上报登录态不拦截 — 仅新引擎显式 false 时 TUI 门禁关闭）。
    /// </summary>
    public sealed record SessionInfo(string Version, string? Model, string PermissionMode, bool BypassAvailable, string Cwd, string? Vendor = null, bool LoggedIn = true) : BridgeEvent;

    /// <summary>权限模式切换回执（mode 为引擎实际生效值 — 安全闸拦截时与请求值不同）。</summary>
    public sealed record PermissionModeChanged(string Mode) : BridgeEvent;

    /// <summary>回合开始（busy 挂载 spinner）。</summary>
    public sealed record TurnStarted() : BridgeEvent;

    /// <summary>进入思考（spinner Thinking 模式）。</summary>
    public sealed record ThinkingStarted() : BridgeEvent;

    /// <summary>思考结束（驱动 ThinkingLifecycle）。</summary>
    public sealed record ThinkingEnded(long DurationMs) : BridgeEvent;

    /// <summary>工具调用消息（bullet + bold 工具名 + args）。</summary>
    public sealed record ToolCall(string Name, string Args) : BridgeEvent;

    /// <summary>
    /// 工具结果摘要（⎿ 缩进项）；Patch 非空时渲染为结构化 diff 块（M7-2 StructuredDiff）。
    /// Patch null 不输出字段（WhenWritingNull — 与引擎侧 ToolResult 契约逐字节一致，旧 JSON 兼容）。
    /// </summary>
    public sealed record ToolResult(string Summary,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] DiffHunk[]? Patch = null) : BridgeEvent;

    /// <summary>
    /// 权限请求（工具执行前需用户确认 — TUI 模态对话框）。Demo 进程内路径由 SimulatedEngine 事件驱动；
    /// 真实宿主路径不经此事件（TuiPermissionBridge.Confirm 同步回调直挂 ReplShell.BeginPermissionRequest）。
    /// </summary>
    public sealed record PermissionRequest(string ToolName, string Description) : BridgeEvent;

    /// <summary>流式 token 增量（spinner ResponseLength 累加）。</summary>
    public sealed record TokenDelta(int Length) : BridgeEvent;

    /// <summary>
    /// 完整助手消息（追加滚动区）。Markdown 非空时渲染为块模型（M7-4）；
    /// null 不输出字段（WhenWritingNull — 引擎侧旧协议逐字节一致，降级纯文本路径）。
    /// TASK062 结构化：MessageId（回合序号 — Ctrl+O 定位/transcript 对账，UI 不显示数字本身）/
    /// ThinkingDurationMs（思考时长 — 0 = 无思考不渲染思考行）/ ThinkingContent（CoT 全文 —
    /// null 不输出字段，Ctrl+O 展开渲染）；三者末尾可选参数向后兼容（同 SessionInfo.Vendor 先例）。
    /// </summary>
    public sealed record AssistantMessage(string Text,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] MarkdownDocument? Markdown = null,
        int MessageId = 0,
        int ThinkingDurationMs = 0,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? ThinkingContent = null) : BridgeEvent;

    /// <summary>回合完成（idle）。</summary>
    public sealed record TurnCompleted() : BridgeEvent;

    /// <summary>回合被打断（idle）。</summary>
    public sealed record TurnInterrupted() : BridgeEvent;

    /// <summary>
    /// 子代理启动（M7-7 分组渲染入口）— Driver 惰性建组（回合内首个子代理创建组项落位滚动区），
    /// 同回合内全部子代理归一组（分组语义 = 按 turn，基准按 messageId 批次 — 本项目 bridge 协议无消息边界）。
    /// </summary>
    public sealed record SubagentStarted(string SubagentId, string AgentType, string Description) : BridgeEvent;

    /// <summary>
    /// 子代理工具活动（进度增量）— 组内条目计数/令牌累计 + 最后活动更新（基准 agent_progress 流式 append 语义；
    /// 本项目路由到组条目原地更新，不逐条刷屏）。TokenGain = 本次活动新增令牌（参数名避开嵌套类型 TokenDelta）。
    /// </summary>
    public sealed record SubagentToolUse(string SubagentId, string ToolName, string Summary, int TokenGain) : BridgeEvent;

    /// <summary>子代理完成 — 条目定格（isError 聚合组头 ● 红；基准基准 agent 完成态 Done）。</summary>
    public sealed record SubagentFinished(string SubagentId, bool IsError) : BridgeEvent;

    /// <summary>错误（含诱导说明）。</summary>
    public sealed record Error(string Message) : BridgeEvent;
}
