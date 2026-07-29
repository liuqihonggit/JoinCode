namespace McpProtocol;

/// <summary>
/// MCP 协议方法名枚举 — 替代 switch-case 中的硬编码字符串
/// </summary>
public enum McpMethod
{
    // 请求方法
    [EnumValue("initialize")] Initialize,
    [EnumValue("ping")] Ping,
    [EnumValue("tools/list")] ToolsList,
    [EnumValue("tools/call")] ToolsCall,
    [EnumValue("resources/list")] ResourcesList,
    [EnumValue("resources/read")] ResourcesRead,
    [EnumValue("prompts/list")] PromptsList,
    [EnumValue("prompts/get")] PromptsGet,
    [EnumValue("logging/setLevel")] LoggingSetLevel,
    [EnumValue("completion/complete")] CompletionComplete,

    // 服务器到客户端请求方法
    [EnumValue("elicitation/create")] ElicitationCreate,
    [EnumValue("roots/list")] RootsList,
    [EnumValue("sampling/createMessage")] SamplingCreateMessage,

    // 通知方法
    [EnumValue("initialized")] Initialized,
    [EnumValue("notifications/cancelled")] NotificationCancelled,
    [EnumValue("notifications/resources/updated")] NotificationResourcesUpdated,
    [EnumValue("notifications/resources/list_changed")] NotificationResourcesListChanged,
    [EnumValue("notifications/tools/list_changed")] NotificationToolsListChanged,
    [EnumValue("notifications/prompts/list_changed")] NotificationPromptsListChanged,
    [EnumValue("notifications/message")] NotificationMessage,
    [EnumValue("notifications/elicitation_complete")] NotificationElicitationComplete,
    [EnumValue("notifications/progress")] NotificationProgress
}
