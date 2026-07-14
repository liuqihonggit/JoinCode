namespace Mcp.MockServer.Models;

/// <summary>
/// MCP MockServer 配置模型 — 定义监听端口、服务器元数据和工具列表
/// </summary>
public sealed class McpMockServerConfig : MockServerConfigBase<McpMockServerConfig>
{
    /// <summary>服务器名称（返回给客户端的 serverInfo.name）</summary>
    public string ServerName { get; set; } = "JoinCode.Mcp.MockServer";

    /// <summary>服务器版本（返回给客户端的 serverInfo.version）</summary>
    public string ServerVersion { get; set; } = "1.0.0";

    /// <summary>MCP 协议版本</summary>
    public string ProtocolVersion { get; set; } = "2024-11-05";

    /// <summary>Mock 工具列表</summary>
    public List<McpToolDefinition> Tools { get; set; } = [];

    protected override JsonTypeInfo<McpMockServerConfig> JsonTypeInfo => McpMockServerJsonContext.Default.McpMockServerConfig;
    protected override string LogPrefix => "[Mcp.MockServer]";
    protected override string ConfigNotFoundMessage => "MCP MockServer 配置文件不存在: {0}";

    /// <summary>从 JSON 文件加载配置</summary>
    public static McpMockServerConfig LoadFromFile(string path)
        => LoadFromFile(path, McpMockServerJsonContext.Default.McpMockServerConfig, "MCP MockServer 配置文件不存在: {0}");

    /// <summary>从 JSON 文件加载配置 — 文件不存在时返回默认配置</summary>
    public static McpMockServerConfig LoadFromFileOrDefault(string path)
        => LoadFromFileOrDefault(path, McpMockServerJsonContext.Default.McpMockServerConfig, "[Mcp.MockServer]", "MCP MockServer 配置文件不存在: {0}");
}

/// <summary>
/// Mock 工具定义 — 描述工具的元数据和响应行为
/// </summary>
public sealed class McpToolDefinition
{
    /// <summary>工具名称</summary>
    public string Name { get; set; } = "";

    /// <summary>工具描述</summary>
    public string Description { get; set; } = "";

    /// <summary>工具的输入参数 schema（JSON 字符串，作为 JsonElement 返回给客户端）</summary>
    public JsonElement InputSchema { get; set; }

    /// <summary>工具响应模式：echo/echo_args/fixed/echo_json</summary>
    public string ResponseMode { get; set; } = "echo_args";

    /// <summary>固定响应文本（ResponseMode=fixed 时使用）</summary>
    public string? FixedResponse { get; set; }
}
