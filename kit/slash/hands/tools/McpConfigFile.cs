namespace JoinCode.ChatCommands;

/// <summary>
/// MCP 服务器配置条目 — 描述单个 MCP 服务器的传输方式、命令、参数和环境变量
/// </summary>
public sealed class McpServerConfigEntry
{
    /// <summary>传输类型（stdio、sse、http 等），默认 stdio</summary>
    public string Type { get; set; } = "stdio";
    /// <summary>stdio 模式下要执行的命令</summary>
    public string? Command { get; set; }
    /// <summary>命令参数列表</summary>
    public List<string> Args { get; set; } = [];
    /// <summary>http/sse 模式下的服务器 URL</summary>
    public string? Url { get; set; }
    /// <summary>环境变量键值对</summary>
    public Dictionary<string, string> Env { get; set; } = [];
    /// <summary>HTTP 请求头键值对</summary>
    public Dictionary<string, string> Headers { get; set; } = [];
}

/// <summary>
/// MCP 配置文件 — 包含所有 MCP 服务器条目的根容器
/// </summary>
public sealed class McpConfigFile
{
    /// <summary>MCP 服务器名到配置条目的映射</summary>
    public Dictionary<string, McpServerConfigEntry> McpServers { get; set; } = new();
}

/// <summary>
/// MCP 配置 JSON 序列化上下文 — 为 AOT 编译预生成 JSON 序列化代码
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(McpConfigFile))]
[JsonSerializable(typeof(McpServerConfigEntry))]
[JsonSerializable(typeof(Dictionary<string, McpServerConfigEntry>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<string>))]
public partial class McpConfigJsonContext : JsonSerializerContext;
