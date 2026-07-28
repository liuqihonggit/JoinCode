using JoinCode.Abstractions.Attributes;
using Core.Prompts;

namespace Core.Prompts.Sections;

[PromptSection(Name = "glob_rules", Order = 78, IsDynamic = true)]
public static class GlobRulesSection
{
    public static string? GetContent()
    {
        var externalRules = PromptConfigSnapshot.Current.ExternalRules;
        var fileContext = PromptConfigSnapshot.Current.FileContext ?? new FileContextTracker();

        if (externalRules is null || externalRules.Count == 0) return null;

        var filePaths = fileContext.CurrentFilePaths;
        if (filePaths.Count == 0) return null;

        var matchingRules = new List<ExternalRuleEntry>();

        foreach (var rule in externalRules)
        {
            if (string.IsNullOrEmpty(rule.Globs)) continue;

            var patterns = rule.Globs.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            foreach (var pattern in patterns)
            {
                var matched = false;
                foreach (var filePath in filePaths)
                {
                    var fileName = Path.GetFileName(filePath);
                    if (MatchesGlobPattern(fileName, pattern) || MatchesGlobPattern(filePath, pattern))
                    {
                        matchingRules.Add(rule);
                        matched = true;
                        break;
                    }
                }

                if (matched) break;
            }
        }

        if (matchingRules.Count == 0) return null;

        var sb = new StringBuilder();
        sb.AppendLine("# 文件相关规则");
        sb.AppendLine();
        sb.AppendLine("以下规则与当前操作的文件相关（基于 glob 模式匹配）：");
        sb.AppendLine();

        foreach (var rule in matchingRules)
        {
            sb.AppendLine($"## {rule.Name}");
            if (!string.IsNullOrEmpty(rule.Description))
            {
                sb.AppendLine($"（适用场景: {rule.Description}）");
            }
            sb.AppendLine();
            sb.AppendLine(rule.Content);
            sb.AppendLine();
        }

        sb.AppendLine("这些规则与当前操作的文件相关，请在处理相关文件时遵循。");

        return sb.ToString();
    }

    public static SystemPromptSection Create() =>
        SystemPromptSection.Dynamic("glob_rules", GetContent);

    private static bool MatchesGlobPattern(string input, string pattern)
    {
        if (string.IsNullOrEmpty(pattern) || string.IsNullOrEmpty(input)) return false;

        if (!pattern.Contains('*', StringComparison.Ordinal) && !pattern.Contains('?', StringComparison.Ordinal))
        {
            return input.Equals(pattern, StringComparison.OrdinalIgnoreCase);
        }

        var regexPattern = Regex.Escape(pattern)
            .Replace("\\*", ".*", StringComparison.Ordinal)
            .Replace("\\?", ".", StringComparison.Ordinal);

        try
        {
            return Regex.IsMatch(input, $"^{regexPattern}$", RegexOptions.IgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
