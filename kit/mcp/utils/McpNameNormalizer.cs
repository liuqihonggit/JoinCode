namespace McpClient;

public static partial class McpNameNormalizer
{
    private const string ClaudeAiServerPrefix = "claude.ai ";

    public static string NormalizeNameForMCP(string name)
    {
        return NameNormalizer.NormalizeForMcp(name);
    }

    public static string GetMcpPrefix(string serverName)
    {
        return $"mcp__{NormalizeNameForMCP(serverName)}__";
    }

    public static string BuildMcpToolName(string serverName, string toolName)
    {
        return $"{GetMcpPrefix(serverName)}{NormalizeNameForMCP(toolName)}";
    }

    public static (string ServerName, string? ToolName)? McpInfoFromString(string toolString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toolString);

        var parts = toolString.Split("__");
        if (parts.Length < 2 || parts[0] != "mcp" || string.IsNullOrEmpty(parts[1]))
        {
            return null;
        }

        var serverName = parts[1];
        string? toolName = parts.Length > 2 ? string.Join("__", parts[2..]) : null;

        return (serverName, toolName);
    }

    public static string GetMcpDisplayName(string fullName, string serverName)
    {
        var prefix = $"mcp__{NormalizeNameForMCP(serverName)}__";
        return fullName.StartsWith(prefix, StringComparison.Ordinal)
            ? fullName[prefix.Length..]
            : fullName;
    }
}