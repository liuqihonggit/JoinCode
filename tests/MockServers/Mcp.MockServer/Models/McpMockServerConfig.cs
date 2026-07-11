namespace Mcp.MockServer.Models;

/// <summary>
/// MCP MockServer 配置模型 — 定义监听端口、服务器元数据和工具列表
/// </summary>
public sealed class McpMockServerConfig
{
    /// <summary>监听端口（0 表示自动分配）</summary>
    public int Port { get; set; } = 0;

    /// <summary>服务器名称（返回给客户端的 serverInfo.name）</summary>
    public string ServerName { get; set; } = "JoinCode.Mcp.MockServer";

    /// <summary>服务器版本（返回给客户端的 serverInfo.version）</summary>
    public string ServerVersion { get; set; } = "1.0.0";

    /// <summary>MCP 协议版本</summary>
    public string ProtocolVersion { get; set; } = "2024-11-05";

    /// <summary>Mock 工具列表</summary>
    public List<McpToolDefinition> Tools { get; set; } = [];

    /// <summary>从 JSON 文件加载配置</summary>
    public static McpMockServerConfig LoadFromFile(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        if (!File.Exists(path))
            throw new FileNotFoundException($"MCP MockServer 配置文件不存在: {path}", path);

        var json = File.ReadAllText(path);
        var config = JsonSerializer.Deserialize(json, McpMockServerJsonContext.Default.McpMockServerConfig)
            ?? throw new InvalidOperationException($"配置文件反序列化失败: {path}");
        return config;
    }

    /// <summary>从 JSON 文件加载配置 — 文件不存在时返回默认配置</summary>
    public static McpMockServerConfig LoadFromFileOrDefault(string path)
    {
        var actualPath = ResolveConfigPath(path);
        if (actualPath is null)
            return new McpMockServerConfig();
        try
        {
            return LoadFromFile(actualPath);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[Mcp.MockServer] 加载配置文件失败，使用默认配置: {ex.Message}");
            return new McpMockServerConfig();
        }
    }

    private static string? ResolveConfigPath(string path)
    {
        if (File.Exists(path))
            return path;
        var fileName = Path.GetFileName(path);
        var fallbackPath = Path.Combine(AppContext.BaseDirectory, fileName);
        return File.Exists(fallbackPath) ? fallbackPath : null;
    }
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
