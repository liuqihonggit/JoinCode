namespace NativeTui.Core.Bridge;

/// <summary>
/// bridge 事件（Engine → TUI，读子进程 stdout）— ADR 0141 事件契约：
/// 会话握手（session_info）、权限模式（permission_mode_changed）、
/// 回合生命周期（turn_started/completed/interrupted）、思考（thinking_started/ended）、
/// 工具（tool_call/tool_result）、流式（token_delta）、消息（assistant_message/error）。
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(SessionInfo), "session_info")]
[JsonDerivedType(typeof(PermissionModeChanged), "permission_mode_changed")]
[JsonDerivedType(typeof(TurnStarted), "turn_started")]
[JsonDerivedType(typeof(ThinkingStarted), "thinking_started")]
[JsonDerivedType(typeof(ThinkingEnded), "thinking_ended")]
[JsonDerivedType(typeof(ToolCall), "tool_call")]
[JsonDerivedType(typeof(ToolResult), "tool_result")]
[JsonDerivedType(typeof(TokenDelta), "token_delta")]
[JsonDerivedType(typeof(AssistantMessage), "assistant_message")]
[JsonDerivedType(typeof(TurnCompleted), "turn_completed")]
[JsonDerivedType(typeof(TurnInterrupted), "turn_interrupted")]
[JsonDerivedType(typeof(Error), "error")]
public abstract record BridgeEvent
{
    /// <summary>
    /// 会话握手（引擎启动后首条事件）— 版本/当前模型/权限模式/bypass 可用性/cwd。
    /// Model 为 null 表示引擎未设置模型（banner 显示"当前模型未设置"）。
    /// </summary>
    public sealed record SessionInfo(string Version, string? Model, string PermissionMode, bool BypassAvailable, string Cwd) : BridgeEvent;

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

    /// <summary>工具结果摘要（⎿ 缩进项）。</summary>
    public sealed record ToolResult(string Summary) : BridgeEvent;

    /// <summary>流式 token 增量（spinner ResponseLength 累加）。</summary>
    public sealed record TokenDelta(int Length) : BridgeEvent;

    /// <summary>完整助手消息（追加滚动区）。</summary>
    public sealed record AssistantMessage(string Text) : BridgeEvent;

    /// <summary>回合完成（idle）。</summary>
    public sealed record TurnCompleted() : BridgeEvent;

    /// <summary>回合被打断（idle）。</summary>
    public sealed record TurnInterrupted() : BridgeEvent;

    /// <summary>错误（含诱导说明）。</summary>
    public sealed record Error(string Message) : BridgeEvent;
}
