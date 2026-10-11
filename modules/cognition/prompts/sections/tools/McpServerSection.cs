
namespace Core.Prompts.Sections;

/// <summary>
/// MCP服务器部分 - 关于连接的MCP服务器
/// </summary>
[PromptSection(Name = "mcp_server", Order = 76, IsDynamic = true)]
public static class McpServerSection {
    /// <summary>
    /// 获取 MCP 服务器部分的提示词内容。当无已连接服务器时返回 null。
    /// </summary>
    /// <returns>MCP 服务器说明文本；若无已连接服务器则返回 null。</returns>
    public static string? GetContent() {
        var servers = PromptConfigSnapshot.Current.McpServers.ToList();

        if (servers.Count == 0) {
            return null;
        }

        var result = new System.Text.StringBuilder();
        result.AppendLine("# MCP服务器说明");
        result.AppendLine();
        result.AppendLine("以下MCP服务器已提供关于如何使用其工具和资源的说明：");
        result.AppendLine();

        foreach (var server in servers) {
            result.AppendLine($"## {server}");
            result.AppendLine($"[服务器 {server} 的说明将在连接时加载]");
            result.AppendLine();
        }

        return result.ToString().TrimEnd();
    }

    /// <summary>
    /// 创建 MCP 服务器 Section 实例（动态内容，每次重新生成）。
    /// </summary>
    /// <returns>MCP 服务器 Section 实例。</returns>
    public static SystemPromptSection Create() =>
        SystemPromptSection.Dynamic("mcp_servers", GetContent);
}