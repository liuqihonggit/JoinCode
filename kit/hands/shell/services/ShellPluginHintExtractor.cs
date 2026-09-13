namespace Tools.Shell;

/// <summary>
/// Shell 插件提示 — 从命令输出中提取的插件提示信息
/// </summary>
public sealed class ShellPluginHint
{
    /// <summary>
    /// 提示格式版本
    /// </summary>
    public required int V { get; init; }

    /// <summary>
    /// 提示类型（如 plugin）
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// 提示值
    /// </summary>
    public required string Value { get; init; }

    /// <summary>
    /// 来源命令的首个 token
    /// </summary>
    public required string SourceCommand { get; init; }
}

/// <summary>
/// Shell 插件提示提取结果 — 包含提取到的提示列表和剥离提示标签后的输出
/// </summary>
public sealed class ShellPluginHintExtractionResult
{
    /// <summary>
    /// 提取到的插件提示列表
    /// </summary>
    public required IReadOnlyList<ShellPluginHint> Hints { get; init; }

    /// <summary>
    /// 剥离提示标签后的输出文本
    /// </summary>
    public required string StrippedOutput { get; init; }
}

/// <summary>
/// Shell 插件提示提取器 — 从命令输出中检测并剥离 Claude Code 兼容的插件提示标签
/// </summary>
public static class ShellPluginHintExtractor
{
    private static readonly FrozenSet<int> SupportedVersions = new[] { 1 }.ToFrozenSet();
    private static readonly FrozenSet<string> SupportedTypes = new[] { "plugin" }.ToFrozenSet(StringComparer.Ordinal);

    private static readonly Regex HintTagRe = new(
        $@"^[ \t]*<{ClaudeCompatConstants.XmlClaudeCodeHint}\s+([^>]*?)\s*\/>[ \t]*$",
        RegexOptions.Multiline | RegexOptions.Compiled);

    private static readonly Regex AttrRe = new(
        @"(\w+)=(?:""([^""]*)""|([^\s/>]+))",
        RegexOptions.Compiled);

    /// <summary>
    /// 从命令输出中提取插件提示，并剥离提示标签
    /// </summary>
    /// <param name="output">命令输出文本</param>
    /// <param name="command">来源命令（用于提取首个 token 作为 SourceCommand）</param>
    /// <returns>提取结果，包含提示列表和剥离标签后的输出</returns>
    public static ShellPluginHintExtractionResult Extract(string output, string command)
    {
        if (string.IsNullOrEmpty(output) || !output.Contains(ClaudeCompatConstants.XmlClaudeCodeHint, StringComparison.Ordinal))
        {
            return new ShellPluginHintExtractionResult
            {
                Hints = [],
                StrippedOutput = output ?? string.Empty
            };
        }

        var sourceCommand = FirstCommandToken(command);
        var hints = new List<ShellPluginHint>();

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

            hints.Add(new ShellPluginHint
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

        return new ShellPluginHintExtractionResult
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
