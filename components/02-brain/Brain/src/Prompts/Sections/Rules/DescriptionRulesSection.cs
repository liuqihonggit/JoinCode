using JoinCode.Abstractions.Attributes;
using Core.Prompts;

namespace Core.Prompts.Sections;

[PromptSection(Name = "description_rules", Order = 79, IsDynamic = true)]
public static class DescriptionRulesSection
{
    private static readonly char[] DescriptionSeparators = [' ', ',', '，', '、', ';', '；', '|', '\t', '\n', '\r'];

    public static string? GetContent()
    {
        var externalRules = PromptConfigSnapshot.Current.ExternalRules;
        var fileContext = PromptConfigSnapshot.Current.FileContext ?? new FileContextTracker();

        if (externalRules is null || externalRules.Count == 0) return null;

        var userMessage = fileContext.CurrentUserMessage;
        if (string.IsNullOrWhiteSpace(userMessage)) return null;

        var messageKeywords = ExtractKeywords(userMessage);
        if (messageKeywords.Count == 0) return null;

        var matchingRules = new List<ExternalRuleEntry>();

        foreach (var rule in externalRules)
        {
            if (string.IsNullOrEmpty(rule.Description)) continue;
            if (rule.AlwaysApply) continue;
            if (!string.IsNullOrEmpty(rule.Globs)) continue;

            var descKeywords = ExtractKeywords(rule.Description);
            if (descKeywords.Count == 0) continue;

            var overlap = CountOverlap(messageKeywords, descKeywords);
            if (overlap >= Math.Max(1, descKeywords.Count / 2))
            {
                matchingRules.Add(rule);
            }
        }

        if (matchingRules.Count == 0) return null;

        var sb = new StringBuilder();
        sb.AppendLine("# 场景相关规则");
        sb.AppendLine();
        sb.AppendLine("以下规则与当前任务场景相关（基于描述关键词匹配）：");
        sb.AppendLine();

        foreach (var rule in matchingRules)
        {
            sb.AppendLine($"## {rule.Name}");
            sb.AppendLine($"（适用场景: {rule.Description}）");
            sb.AppendLine();
            sb.AppendLine(rule.Content);
            sb.AppendLine();
        }

        sb.AppendLine("这些规则与当前任务场景相关，请在处理相关任务时遵循。");

        return sb.ToString();
    }

    public static SystemPromptSection Create() =>
        SystemPromptSection.Dynamic("description_rules", GetContent);

    internal static List<string> ExtractKeywords(string text)
    {
        var keywords = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var parts = text.Split(DescriptionSeparators, StringSplitOptions.RemoveEmptyEntries);
        foreach (var part in parts)
        {
            var normalized = part.Trim();
            if (normalized.Length < 2) continue;
            if (IsStopWord(normalized)) continue;

            if (ContainsCJK(normalized))
            {
                foreach (var bigram in ExtractCJKBigrams(normalized))
                {
                    if (!IsStopWord(bigram) && seen.Add(bigram)) keywords.Add(bigram);
                }
            }
            else
            {
                if (seen.Add(normalized)) keywords.Add(normalized);
            }
        }

        return keywords;
    }

    private static bool ContainsCJK(string text)
    {
        foreach (var c in text)
        {
            if (c >= 0x4E00 && c <= 0x9FFF) return true;
            if (c >= 0x3400 && c <= 0x4DBF) return true;
        }
        return false;
    }

    private static List<string> ExtractCJKBigrams(string text)
    {
        var bigrams = new List<string>();
        var i = 0;
        while (i < text.Length)
        {
            if (IsCJKChar(text[i]))
            {
                var start = i;
                while (i < text.Length && IsCJKChar(text[i])) i++;
                var segment = text[start..i];
                if (segment.Length >= 2)
                {
                    for (int j = 0; j <= segment.Length - 2; j++)
                    {
                        bigrams.Add(segment[j..(j + 2)]);
                    }
                }
            }
            else
            {
                var start = i;
                while (i < text.Length && !IsCJKChar(text[i])) i++;
                var segment = text[start..i];
                if (segment.Length >= 2) bigrams.Add(segment);
            }
        }
        return bigrams;
    }

    private static bool IsCJKChar(char c)
    {
        return (c >= 0x4E00 && c <= 0x9FFF) || (c >= 0x3400 && c <= 0x4DBF);
    }

    private static int CountOverlap(List<string> left, List<string> right)
    {
        var count = 0;
        foreach (var kw in right)
        {
            foreach (var msg in left)
            {
                if (kw.Equals(msg, StringComparison.OrdinalIgnoreCase)
                    || msg.Contains(kw, StringComparison.OrdinalIgnoreCase)
                    || kw.Contains(msg, StringComparison.OrdinalIgnoreCase))
                {
                    count++;
                    break;
                }
            }
        }
        return count;
    }

    private static bool IsStopWord(string word)
    {
        return word switch
        {
            "的" or "了" or "在" or "是" or "我" or "有" or "和" or "就" or "不" or "人"
                or "都" or "一" or "个" or "上" or "也" or "很" or "到" or "说" or "要" or "去"
                or "你" or "会" or "着" or "没有" or "看" or "好" or "自己" or "这"
                or "the" or "a" or "an" or "is" or "are" or "was" or "were" or "be" or "been"
                or "being" or "have" or "has" or "had" or "do" or "does" or "did" or "will"
                or "would" or "could" or "should" or "may" or "might" or "can" or "shall"
                or "to" or "of" or "in" or "for" or "on" or "with" or "at" or "by" or "from"
                or "or" or "and" or "but" or "not" or "no" or "if" or "so" or "as" or "it"
                or "that" or "this" or "me" or "my" or "we" or "our" or "you" or "your"
                => true,
            _ => false
        };
    }
}
