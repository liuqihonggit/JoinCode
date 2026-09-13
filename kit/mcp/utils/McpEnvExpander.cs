namespace McpClient;

/// <summary>
/// MCP 环境变量展开器 — 提供 ${VAR} 和 ${VAR:-default} 格式的环境变量展开功能
/// </summary>
public static partial class McpEnvExpander
{
    /// <summary>
    /// 展开字符串中的环境变量 — 支持 ${VAR} 和 ${VAR:-default} 两种格式
    /// </summary>
    /// <param name="value">包含环境变量引用的字符串</param>
    /// <returns>展开后的字符串和未找到的变量名列表</returns>
    public static (string Expanded, List<string> MissingVars) ExpandEnvVarsInString(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var missingVars = new List<string>();

        var expanded = EnvVarRegex().Replace(value, match =>
        {
            var varContent = match.Groups[1].Value;

            var separatorIndex = varContent.IndexOf(":-", StringComparison.Ordinal);
            string varName;
            string? defaultValue = null;

            if (separatorIndex >= 0)
            {
                varName = varContent[..separatorIndex];
                defaultValue = varContent[(separatorIndex + 2)..];
            }
            else
            {
                varName = varContent;
            }

            var envValue = Environment.GetEnvironmentVariable(varName);

            if (envValue is not null)
            {
                return envValue;
            }

            if (defaultValue is not null)
            {
                return defaultValue;
            }

            missingVars.Add(varName);
            return match.Value;
        });

        return (expanded, missingVars);
    }

    /// <summary>
    /// 展开环境变量字典中所有值包含的环境变量引用
    /// </summary>
    /// <param name="environment">环境变量字典（可选）</param>
    /// <returns>展开后的环境变量字典</returns>
    public static Dictionary<string, string> ExpandEnvironmentValues(Dictionary<string, string>? environment)
    {
        if (environment is null || environment.Count == 0)
        {
            return environment ?? new Dictionary<string, string>();
        }

        var expanded = new Dictionary<string, string>(environment.Count);
        foreach (var kvp in environment)
        {
            if (kvp.Value.Contains('$'))
            {
                var (value, _) = ExpandEnvVarsInString(kvp.Value);
                expanded[kvp.Key] = value;
            }
            else
            {
                expanded[kvp.Key] = kvp.Value;
            }
        }

        return expanded;
    }

    /// <summary>
    /// 展开端点字符串中的环境变量引用
    /// </summary>
    /// <param name="endpoint">包含环境变量引用的端点字符串</param>
    /// <returns>展开后的端点字符串</returns>
    public static string ExpandEndpoint(string endpoint)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint);

        if (!endpoint.Contains('$'))
        {
            return endpoint;
        }

        var (expanded, _) = ExpandEnvVarsInString(endpoint);
        return expanded;
    }

    [GeneratedRegex(@"\$\{([^}]+)\}", RegexOptions.Compiled)]
    private static partial Regex EnvVarRegex();
}