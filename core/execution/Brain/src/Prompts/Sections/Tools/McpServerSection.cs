
namespace Core.Prompts.Sections;

/// <summary>
/// MCP服务器部分 - 关于连接的MCP服务器
/// </summary>
[PromptSection(Name = "mcp_server", Order = 76, IsDynamic = true)]
public static class McpServerSection {
    public static string? GetContent() {
        var servers = PromptConfigSnapshot.Current.McpServers?.ToList();

        if (servers == null || servers.Count == 0) {
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

    public static SystemPromptSection Create() =>
        SystemPromptSection.Dynamic("mcp_servers", GetContent);
}
