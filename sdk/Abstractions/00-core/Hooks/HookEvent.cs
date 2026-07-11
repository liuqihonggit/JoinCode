namespace JoinCode.Abstractions.Hooks;

/// <summary>
/// 钩子事件类型
/// 参考 Claude Code 的 HookEvent 设计
/// </summary>
public enum HookEvent
{
    // ========== 工具使用相关 ==========
    /// <summary>工具使用前</summary>
    [EnumValue("preToolUse")] PreToolUse,

    /// <summary>工具使用后</summary>
    [EnumValue("postToolUse")] PostToolUse,

    /// <summary>工具使用失败后</summary>
    [EnumValue("postToolUseFailure")] PostToolUseFailure,

    /// <summary>权限被拒绝后</summary>
    [EnumValue("permissionDenied")] PermissionDenied,

    /// <summary>权限请求时</summary>
    [EnumValue("permissionRequest")] PermissionRequest,

    // ========== 会话生命周期 ==========
    /// <summary>会话开始时</summary>
    [EnumValue("sessionStart")] SessionStart,

    /// <summary>会话结束时</summary>
    [EnumValue("sessionEnd")] SessionEnd,

    /// <summary>响应结束前</summary>
    [EnumValue("stop")] Stop,

    /// <summary>响应结束失败时</summary>
    [EnumValue("stopFailure")] StopFailure,

    /// <summary>设置/初始化时</summary>
    [EnumValue("setup")] Setup,

    // ========== 子代理相关 ==========
    /// <summary>子代理开始时</summary>
    [EnumValue("subagentStart")] SubagentStart,

    /// <summary>子代理结束前</summary>
    [EnumValue("subagentStop")] SubagentStop,

    // ========== 用户交互 ==========
    /// <summary>用户提交提示时</summary>
    [EnumValue("userPromptSubmit")] UserPromptSubmit,

    /// <summary>通知发送时</summary>
    [EnumValue("notification")] Notification,

    // ========== 压缩相关 ==========
    /// <summary>压缩前</summary>
    [EnumValue("preCompact")] PreCompact,

    /// <summary>压缩后</summary>
    [EnumValue("postCompact")] PostCompact,

    // ========== 采样后 ==========
    /// <summary>LLM 采样完成后（每轮对话结束后触发）</summary>
    [EnumValue("postSampling")] PostSampling,

    // ========== 任务管理 ==========
    /// <summary>队友空闲时</summary>
    [EnumValue("teammateIdle")] TeammateIdle,

    /// <summary>任务创建时</summary>
    [EnumValue("taskCreated")] TaskCreated,

    /// <summary>任务完成时</summary>
    [EnumValue("taskCompleted")] TaskCompleted,

    // ========== MCP 相关 ==========
    /// <summary>MCP 请求用户输入时</summary>
    [EnumValue("elicitation")] Elicitation,

    /// <summary>MCP 用户响应后</summary>
    [EnumValue("elicitationResult")] ElicitationResult,

    // ========== 配置变更 ==========
    /// <summary>配置变更时</summary>
    [EnumValue("configChange")] ConfigChange,

    /// <summary>指令文件加载时</summary>
    [EnumValue("instructionsLoaded")] InstructionsLoaded,

    /// <summary>工作目录变更后</summary>
    [EnumValue("cwdChanged")] CwdChanged,

    /// <summary>监视文件变更时</summary>
    [EnumValue("fileChanged")] FileChanged,

    // ========== 工作树 ==========
    /// <summary>工作树创建时</summary>
    [EnumValue("worktreeCreate")] WorktreeCreate,

    /// <summary>工作树移除时</summary>
    [EnumValue("worktreeRemove")] WorktreeRemove
}

/// <summary>
/// HookEvent 扩展方法
/// </summary>
public static class HookEventDisplayExtensions
{
    public static string ToEventName(this HookEvent hookEvent)
    {
        return hookEvent.ToString();
    }

    // 匹配器字段常量
    private const string ToolNameField = "tool_name";
    private const string NotificationTypeField = "notification_type";
    private const string SourceField = "source";
    private const string ErrorField = "error";
    private const string AgentTypeField = "agent_type";
    private const string TriggerField = "trigger";
    private const string ReasonField = "reason";
    private const string McpServerNameField = "mcp_server_name";
    private const string LoadReasonField = "load_reason";

    /// <summary>
    /// 获取事件的匹配器元数据字段
    /// </summary>
    public static string? GetMatcherField(this HookEvent hookEvent)
    {
        return hookEvent switch
        {
            HookEvent.PreToolUse => ToolNameField,
            HookEvent.PostToolUse => ToolNameField,
            HookEvent.PostToolUseFailure => ToolNameField,
            HookEvent.PermissionDenied => ToolNameField,
            HookEvent.PermissionRequest => ToolNameField,
            HookEvent.Notification => NotificationTypeField,
            HookEvent.SessionStart => SourceField,
            HookEvent.StopFailure => ErrorField,
            HookEvent.SubagentStart => AgentTypeField,
            HookEvent.SubagentStop => AgentTypeField,
            HookEvent.PreCompact => TriggerField,
            HookEvent.PostCompact => TriggerField,
            HookEvent.PostSampling => SourceField,
            HookEvent.SessionEnd => ReasonField,
            HookEvent.Setup => TriggerField,
            HookEvent.Elicitation => McpServerNameField,
            HookEvent.ElicitationResult => McpServerNameField,
            HookEvent.ConfigChange => SourceField,
            HookEvent.InstructionsLoaded => LoadReasonField,
            _ => null
        };
    }

    /// <summary>
    /// 检查事件是否需要匹配器
    /// </summary>
    public static bool RequiresMatcher(this HookEvent hookEvent)
    {
        return GetMatcherField(hookEvent) != null;
    }

    /// <summary>
    /// 检查事件是否支持阻塞
    /// </summary>
    public static bool SupportsBlocking(this HookEvent hookEvent)
    {
        return hookEvent switch
        {
            HookEvent.PreToolUse => true,
            HookEvent.UserPromptSubmit => true,
            HookEvent.PreCompact => true,
            HookEvent.PermissionRequest => true,
            HookEvent.Elicitation => true,
            HookEvent.ElicitationResult => true,
            HookEvent.ConfigChange => true,
            HookEvent.TaskCreated => true,
            HookEvent.TaskCompleted => true,
            _ => false
        };
    }
}
