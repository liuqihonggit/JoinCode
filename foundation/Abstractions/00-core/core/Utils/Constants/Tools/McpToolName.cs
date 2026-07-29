namespace JoinCode.Abstractions.Utils;

/// <summary>
/// MCP 客户端/认证工具名称枚举
/// </summary>
public enum McpToolName
{
    [EnumValue("mcp_list_servers")] McpListServers,
    [EnumValue("mcp_connect")] McpConnect,
    [EnumValue("mcp_disconnect")] McpDisconnect,
    [EnumValue("mcp_list_tools")] McpListTools,
    [EnumValue("mcp_call_tool")] McpCallTool,
    [EnumValue("ListMcpResourcesTool")] McpListResources,
    [EnumValue("ReadMcpResourceTool")] McpReadResource,
    [EnumValue("mcp_list_prompts")] McpListPrompts,
    [EnumValue("mcp_remote_list_resources")] McpRemoteListResources,
    [EnumValue("mcp_remote_read_resource")] McpRemoteReadResource,
    [EnumValue("mcp_remote_list_prompts")] McpRemoteListPrompts,
    [EnumValue("mcp_get_prompt")] McpGetPrompt,
    [EnumValue("mcp_list_clients")] McpListClients,
    [EnumValue("mcp_auth_apikey")] McpAuthApiKey,
    [EnumValue("mcp_auth_bearer")] McpAuthBearer,
    [EnumValue("mcp_auth_basic")] McpAuthBasic,
    [EnumValue("mcp_auth_oauth2")] McpAuthOAuth2,
    [EnumValue("mcp_auth_refresh")] McpAuthRefresh,
    [EnumValue("mcp_auth_status")] McpAuthStatus,
    [EnumValue("mcp_auth_remove")] McpAuthRemove,
    [EnumValue("RemoteTrigger")] McpRemoteTrigger,
}
