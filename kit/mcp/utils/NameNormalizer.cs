namespace McpClient;

/// <summary>
/// 名称规范化器 — 将名称中的非法字符替换为指定字符，并截断到最大长度
/// </summary>
public static partial class NameNormalizer {
    private const int DefaultMaxNameLength = 64;

    /// <summary>
    /// 将名称规范化为 MCP 兼容格式 — 替换非法字符为指定字符，截断到最大长度
    /// </summary>
    /// <param name="name">待规范化的名称</param>
    /// <param name="replacement">非法字符替换字符（默认 '_'）</param>
    /// <param name="maxLength">最大长度（默认 64）</param>
    /// <returns>规范化后的名称</returns>
    public static string NormalizeForMcp(string name, char replacement = '_', int maxLength = DefaultMaxNameLength) {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var normalized = InvalidCharsRegex().Replace(name, replacement.ToString());

        if (normalized.StartsWith("claude.ai ", StringComparison.Ordinal)) {
            normalized = MultipleRepeatsRegex(replacement).Replace(normalized, replacement.ToString());
            normalized = normalized.Trim(replacement);
        }

        if (normalized.Length > maxLength) {
            normalized = normalized[..maxLength];
        }

        return normalized;
    }

    [GeneratedRegex(@"[^a-zA-Z0-9_-]")]
    private static partial Regex InvalidCharsRegex();

    private static Regex MultipleRepeatsRegex(char c) => new($"{Regex.Escape(c.ToString())}+", RegexOptions.Compiled);
}