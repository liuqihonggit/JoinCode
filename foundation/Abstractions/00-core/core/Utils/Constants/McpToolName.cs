namespace JoinCode.Abstractions.Utils;

/// <summary>
/// MCP 客户端/认证工具名称枚举
/// </summary>
public enum McpToolName
{
    [EnumValue("mcp_list_servers")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    McpListServers,

    [EnumValue("mcp_connect")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    McpConnect,

    [EnumValue("mcp_disconnect")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    McpDisconnect,

    [EnumValue("mcp_list_tools")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    McpListTools,

    [EnumValue("mcp_call_tool")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    McpCallTool,

    [EnumValue("ListMcpResourcesTool")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    McpListResources,

    [EnumValue("ReadMcpResourceTool")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    McpReadResource,

    [EnumValue("mcp_list_prompts")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    McpListPrompts,

    [EnumValue("mcp_remote_list_resources")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    McpRemoteListResources,

    [EnumValue("mcp_remote_read_resource")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    McpRemoteReadResource,

    [EnumValue("mcp_remote_list_prompts")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    McpRemoteListPrompts,

    [EnumValue("mcp_get_prompt")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    McpGetPrompt,

    [EnumValue("mcp_list_clients")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    McpListClients,

    [EnumValue("mcp_auth_apikey")]
    [SecurityClass("sensitive", AutoDenied = true, PlanDenied = true, AskAllowed = true)]
    McpAuthApiKey,

    [EnumValue("mcp_auth_bearer")]
    [SecurityClass("sensitive", AutoDenied = true, PlanDenied = true, AskAllowed = true)]
    McpAuthBearer,

    [EnumValue("mcp_auth_basic")]
    [SecurityClass("sensitive", AutoDenied = true, PlanDenied = true, AskAllowed = true)]
    McpAuthBasic,

    [EnumValue("mcp_auth_oauth2")]
    [SecurityClass("sensitive", AutoDenied = true, PlanDenied = true, AskAllowed = true)]
    McpAuthOAuth2,

    [EnumValue("mcp_auth_refresh")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    McpAuthRefresh,

    [EnumValue("mcp_auth_status")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    McpAuthStatus,

    [EnumValue("mcp_auth_remove")]
    [SecurityClass("sensitive", AutoDenied = true, PlanDenied = true, AskAllowed = true)]
    McpAuthRemove,

    [EnumValue("RemoteTrigger")]
    [SecurityClass("safe-write", AutoAllowed = true, PlanAllowed = false, AskAllowed = true)]
    McpRemoteTrigger,

    [EnumValue("MCP")]
    [SecurityClass("readonly", AutoAllowed = true, PlanAllowed = true, AskAllowed = true)]
    MCP,
}
