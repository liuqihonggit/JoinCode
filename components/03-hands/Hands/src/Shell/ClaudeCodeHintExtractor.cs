namespace Tools.Shell;

public sealed class ClaudeCodeHint
{
    public required int V { get; init; }
    public required string Type { get; init; }
    public required string Value { get; init; }
    public required string SourceCommand { get; init; }
}

public sealed class ClaudeCodeHintExtractionResult
{
    public required IReadOnlyList<ClaudeCodeHint> Hints { get; init; }
    public required string StrippedOutput { get; init; }
}

public static class ClaudeCodeHintExtractor
{
    private static readonly FrozenSet<int> SupportedVersions = new[] { 1 }.ToFrozenSet();
    private static readonly FrozenSet<string> SupportedTypes = new[] { "plugin" }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly Regex HintTagRe = new(
        @"^[ \t]*<claude-code-hint\s+([^>]*?)\s*\/>[ \t]*$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex AttrRe = new(
        @"(\w+)=(?:""([^""]*)""|([^\s/>]+))",
        RegexOptions.Compiled);

    public static ClaudeCodeHintExtractionResult Extract(string output, string command)
    {
        if (string.IsNullOrEmpty(output) || !output.Contains("<claude-code-hint", StringComparison.Ordinal))
        {
            return new ClaudeCodeHintExtractionResult
            {
                Hints = [],
                StrippedOutput = output ?? string.Empty
            };
        }

        var sourceCommand = FirstCommandToken(command);
        var hints = new List<ClaudeCodeHint>();

        var stripped = HintTagRe.Replace(output, match =>
        {
            var attrs = ParseAttrs(match.Value);
            var vStr = attrs.GetValueOrDefault("v", "");
            var type = attrs.GetValueOrDefault("type", "");
            var value = attrs.GetValueOrDefault("value", "");

            if (!int.TryParse(vStr, out var v) || !SupportedVersions.Contains(v))
                return string.Empty;

            if (string.IsNullOrEmpty(type) || !SupportedTypes.Contains(type))
                return string.Empty;

            if (string.IsNullOrEmpty(value))
                return string.Empty;

            hints.Add(new ClaudeCodeHint
            {
                V = v,
                Type = type,
                Value = value,
                SourceCommand = sourceCommand
            });

            return string.Empty;
        });

        if (hints.Count > 0 || stripped != output)
        {
            stripped = CollapseExcessiveBlankLines(stripped);
        }

        return new ClaudeCodeHintExtractionResult
        {
            Hints = hints,
            StrippedOutput = stripped
        };
    }

    private static Dictionary<string, string> ParseAttrs(string tagBody)
    {
        var attrs = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Match m in AttrRe.Matches(tagBody))
        {
            var key = m.Groups[1].Value;
            var value = m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value;
            attrs[key] = value;
        }
        return attrs;
    }

    private static string FirstCommandToken(string command)
    {
        if (string.IsNullOrEmpty(command)) return string.Empty;
        var trimmed = command.TrimStart();
        var spaceIdx = trimmed.IndexOf(' ');
        return spaceIdx < 0 ? trimmed : trimmed[..spaceIdx];
    }

    private static string CollapseExcessiveBlankLines(string text)
    {
        for (var i = 0; i < text.Length - 2;)
        {
            if (text[i] == '\n' && text[i + 1] == '\n' && text[i + 2] == '\n')
            {
                var end = i + 2;
                while (end < text.Length && text[end] == '\n') end++;
                text = text[..i] + "\n\n" + text[end..];
            }
            else
            {
                i++;
            }
        }
        return text;
    }
}
