namespace McpProtocol;

/// <summary>
/// MCP 协议方法名枚举 — 替代 switch-case 中的硬编码字符串
/// </summary>
public enum McpMethod {
    // 请求方法

    /// <summary>初始化握手请求 — 客户端与服务器交换能力与协议版本。</summary>
    [EnumValue("initialize")] Initialize,

    /// <summary>Ping 心跳请求 — 用于连接保活与可用性探测。</summary>
    [EnumValue("ping")] Ping,

    /// <summary>列出服务器可用工具 — tools/list 请求。</summary>
    [EnumValue("tools/list")] ToolsList,

    /// <summary>调用服务器工具 — tools/call 请求。</summary>
    [EnumValue("tools/call")] ToolsCall,

    /// <summary>列出服务器可用资源 — resources/list 请求。</summary>
    [EnumValue("resources/list")] ResourcesList,

    /// <summary>读取指定资源内容 — resources/read 请求。</summary>
    [EnumValue("resources/read")] ResourcesRead,

    /// <summary>列出服务器可用提示模板 — prompts/list 请求。</summary>
    [EnumValue("prompts/list")] PromptsList,

    /// <summary>获取指定提示模板内容 — prompts/get 请求。</summary>
    [EnumValue("prompts/get")] PromptsGet,

    /// <summary>设置服务器日志级别 — logging/setLevel 请求。</summary>
    [EnumValue("logging/setLevel")] LoggingSetLevel,

    /// <summary>补全请求 — completion/complete 请求,用于参数自动补全。</summary>
    [EnumValue("completion/complete")] CompletionComplete,

    // 服务器到客户端请求方法

    /// <summary>Elicitation 请求 — 服务器向客户端发起表单/URL 征询。</summary>
    [EnumValue("elicitation/create")] ElicitationCreate,

    /// <summary>列出客户端根目录 — roots/list 请求,服务器查询客户端可用根。</summary>
    [EnumValue("roots/list")] RootsList,

    /// <summary>采样请求 — 服务器请求客户端创建 LLM 采样消息。</summary>
    [EnumValue("sampling/createMessage")] SamplingCreateMessage,

    // 通知方法

    /// <summary>初始化完成通知 — 握手后客户端告知服务器初始化已结束。</summary>
    [EnumValue("initialized")] Initialized,

    /// <summary>请求取消通知 — 通知对方取消指定 id 的请求。</summary>
    [EnumValue("notifications/cancelled")] NotificationCancelled,

    /// <summary>资源更新通知 — 服务器告知指定资源内容已变更。</summary>
    [EnumValue("notifications/resources/updated")] NotificationResourcesUpdated,

    /// <summary>资源列表变更通知 — 服务器告知可用资源列表已变化。</summary>
    [EnumValue("notifications/resources/list_changed")] NotificationResourcesListChanged,

    /// <summary>工具列表变更通知 — 服务器告知可用工具列表已变化。</summary>
    [EnumValue("notifications/tools/list_changed")] NotificationToolsListChanged,

    /// <summary>提示模板列表变更通知 — 服务器告知可用提示模板列表已变化。</summary>
    [EnumValue("notifications/prompts/list_changed")] NotificationPromptsListChanged,

    /// <summary>消息通知 — 服务器向客户端推送日志/消息。</summary>
    [EnumValue("notifications/message")] NotificationMessage,

    /// <summary>Elicitation 完成通知 — 通知对方 Elicitation 请求已处理完毕。</summary>
    [EnumValue("notifications/elicitation_complete")] NotificationElicitationComplete,

    /// <summary>进度通知 — 长时操作推送进度信息。</summary>
    [EnumValue("notifications/progress")] NotificationProgress
}