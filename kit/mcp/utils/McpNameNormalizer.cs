namespace McpClient;

/// <summary>
/// MCP 名称规范化器 — 提供 MCP 工具名称的构建、解析、显示名获取功能
/// </summary>
public static partial class McpNameNormalizer {
    private const string ClaudeAiServerPrefix = "claude.ai ";

    /// <summary>
    /// 将服务器名称规范化为 MCP 兼容格式
    /// </summary>
    /// <param name="name">待规范化的服务器名称</param>
    /// <returns>规范化后的名称</returns>
    public static string NormalizeNameForMCP(string name) {
        return NameNormalizer.NormalizeForMcp(name);
    }

    /// <summary>
    /// 获取 MCP 工具名称前缀 — 格式为 mcp__{serverName}__
    /// </summary>
    /// <param name="serverName">服务器名称</param>
    /// <returns>MCP 工具名称前缀</returns>
    public static string GetMcpPrefix(string serverName) {
        return $"mcp__{NormalizeNameForMCP(serverName)}__";
    }

    /// <summary>
    /// 构建 MCP 工具全名 — 格式为 mcp__{serverName}__{toolName}
    /// </summary>
    /// <param name="serverName">服务器名称</param>
    /// <param name="toolName">工具名称</param>
    /// <returns>MCP 工具全名</returns>
    public static string BuildMcpToolName(string serverName, string toolName) {
        return $"{GetMcpPrefix(serverName)}{NormalizeNameForMCP(toolName)}";
    }

    /// <summary>
    /// 从 MCP 工具字符串中解析出服务器名称和工具名称
    /// </summary>
    /// <param name="toolString">MCP 工具字符串（格式 mcp__{server}__{tool}）</param>
    /// <returns>服务器名称和工具名称元组；格式不匹配时为 null</returns>
    public static (string ServerName, string? ToolName)? McpInfoFromString(string toolString) {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolString);

        var parts = toolString.Split("__");
        if (parts.Length < 2 || parts[0] != "mcp" || string.IsNullOrEmpty(parts[1])) {
            return null;
        }

        var serverName = parts[1];
        var toolName = parts.Length > 2 ? string.Join("__", parts[2..]) : null;

        return (serverName, toolName);
    }

    /// <summary>
    /// 获取 MCP 工具的显示名称 — 去除服务器前缀后的工具名称
    /// </summary>
    /// <param name="fullName">工具全名</param>
    /// <param name="serverName">服务器名称</param>
    /// <returns>去除前缀后的显示名称；若无前缀则返回原名称</returns>
    public static string GetMcpDisplayName(string fullName, string serverName) {
        var prefix = $"mcp__{NormalizeNameForMCP(serverName)}__";
        return fullName.StartsWith(prefix, StringComparison.Ordinal)
            ? fullName[prefix.Length..]
            : fullName;
    }
}