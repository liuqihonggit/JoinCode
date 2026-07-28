namespace McpClient;

public static partial class NameNormalizer
{
    private const int DefaultMaxNameLength = 64;

    public static string NormalizeForMcp(string name, char replacement = '_', int maxLength = DefaultMaxNameLength)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        var normalized = InvalidCharsRegex().Replace(name, replacement.ToString());

        if (normalized.StartsWith("claude.ai ", StringComparison.Ordinal))
        {
            normalized = MultipleRepeatsRegex(replacement).Replace(normalized, replacement.ToString());
            normalized = normalized.Trim(replacement);
        }

        if (normalized.Length > maxLength)
        {
            normalized = normalized[..maxLength];
        }

        return normalized;
    }

    [GeneratedRegex(@"[^a-zA-Z0-9_-]")]
    private static partial Regex InvalidCharsRegex();

    private static Regex MultipleRepeatsRegex(char c) => new($"{Regex.Escape(c.ToString())}+", RegexOptions.Compiled);
}
