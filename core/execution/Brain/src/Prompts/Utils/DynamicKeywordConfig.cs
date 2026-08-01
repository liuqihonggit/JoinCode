using System.Text.Json.Serialization;

namespace Core.Prompts.Utils;

/// <summary>
/// 动态关键词词表配置模型 — 从 ~/.jcc/keyword-sections.json 加载
/// </summary>
public sealed record DynamicKeywordConfig
{
    /// <summary>
    /// 关键词 Section 配置字典 — Key 为 Section 名称，Value 为关键词列表
    /// </summary>
    [JsonPropertyName("sections")]
    public Dictionary<string, DynamicKeywordSection> Sections { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// 单个关键词 Section 配置
/// </summary>
public sealed record DynamicKeywordSection
{
    /// <summary>
    /// 触发关键词列表（最小词根，如 "睡觉" 而非 "我去睡觉了"）
    /// </summary>
    [JsonPropertyName("keywords")]
    public List<string> Keywords { get; init; } = [];

    /// <summary>
    /// 是否启用，默认 true
    /// </summary>
    [JsonPropertyName("enabled")]
    public bool Enabled { get; init; } = true;

    /// <summary>
    /// 自定义注入内容（可选）— 为空时使用内置 Section 内容
    /// </summary>
    [JsonPropertyName("custom_content")]
    public string? CustomContent { get; init; }
}

/// <summary>
/// 用户输入切词器 — 将自然语言句子切分为词序列，供关键词精确匹配
/// 两阶段策略：1) 标点/空格粗切 → 2) 关键词词表 FMM（Forward Maximum Match）精细切词
/// </summary>
public static class InputTokenizer
{
    private static readonly SearchValues<char> SegmentSeparators = SearchValues.Create(
        " \t\n\r，。！？、；：\u201C\u201D\u2018\u2019（）【】《》—…·~`!@#$%^&*()-_=+[]{}|\\;:'\",.<>?/");

    /// <summary>
    /// 对用户输入做切词，返回词序列
    /// </summary>
    /// <param name="input">用户原始输入</param>
    /// <param name="dictionary">关键词词表（用于 FMM 精细切词）</param>
    public static string[] Tokenize(string input, IReadOnlySet<string> dictionary)
    {
        if (string.IsNullOrWhiteSpace(input))
            return [];

        var segments = CoarseSplit(input.AsSpan());
        if (segments.Count == 0)
            return [];

        if (dictionary.Count == 0)
            return [.. segments];

        var tokens = new List<string>(segments.Count * 2);
        var maxLen = 0;
        var hasMultiWordKeywords = false;
        foreach (var kw in dictionary)
        {
            if (kw.Length > maxLen)
                maxLen = kw.Length;
            if (!hasMultiWordKeywords && ContainsSeparator(kw.AsSpan()))
                hasMultiWordKeywords = true;
        }

        if (hasMultiWordKeywords)
        {
            foreach (var kw in dictionary)
            {
                if (!ContainsSeparator(kw.AsSpan()))
                    continue;

                if (input.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    tokens.Add(kw);
            }
        }

        foreach (var seg in segments)
        {
            if (seg.Length == 0)
                continue;

            FmmTokenize(seg.AsSpan(), dictionary, maxLen, tokens);
        }

        return tokens.ToArray();
    }

    private static bool ContainsSeparator(ReadOnlySpan<char> s)
    {
        for (var i = 0; i < s.Length; i++)
        {
            if (SegmentSeparators.Contains(s[i]))
                return true;
        }
        return false;
    }

    private static List<string> CoarseSplit(ReadOnlySpan<char> input)
    {
        var result = new List<string>();
        var start = 0;

        for (var i = 0; i < input.Length; i++)
        {
            if (SegmentSeparators.Contains(input[i]))
            {
                if (i > start)
                    result.Add(input.Slice(start, i - start).ToString());

                start = i + 1;
            }
        }

        if (start < input.Length)
            result.Add(input.Slice(start).ToString());

        return result;
    }

    private static void FmmTokenize(ReadOnlySpan<char> span, IReadOnlySet<string> dictionary, int maxDictLen, List<string> tokens)
    {
        var pos = 0;

        while (pos < span.Length)
        {
            var remaining = span.Length - pos;
            var matchLen = Math.Min(remaining, maxDictLen);
            var matched = false;

            for (var len = matchLen; len >= 1; len--)
            {
                var candidate = span.Slice(pos, len);
                var candidateStr = candidate.ToString();

                if (dictionary.Contains(candidateStr))
                {
                    tokens.Add(candidateStr);
                    pos += len;
                    matched = true;
                    break;
                }
            }

            if (!matched)
            {
                var ch = span[pos];
                if (char.IsAsciiLetter(ch))
                {
                    var wordStart = pos;
                    while (pos < span.Length && char.IsAsciiLetter(span[pos]))
                        pos++;

                    tokens.Add(span.Slice(wordStart, pos - wordStart).ToString());
                }
                else if (char.IsAsciiDigit(ch))
                {
                    var numStart = pos;
                    while (pos < span.Length && char.IsAsciiDigit(span[pos]))
                        pos++;

                    tokens.Add(span.Slice(numStart, pos - numStart).ToString());
                }
                else
                {
                    tokens.Add(ch.ToString());
                    pos++;
                }
            }
        }
    }
}

/// <summary>
/// 动态关键词匹配器 — 纯逻辑，可独立测试
/// 匹配策略：先切词再逐词精确匹配，避免子串误匹配
/// </summary>
public static class DynamicKeywordMatcher
{
    /// <summary>
    /// 在给定配置中匹配用户输入的关键词
    /// </summary>
    public static DynamicKeywordMatchResult? TryMatch(string input, DynamicKeywordConfig config)
    {
        if (string.IsNullOrWhiteSpace(input))
            return null;

        var dictionary = BuildDictionary(config);
        var tokens = InputTokenizer.Tokenize(input, dictionary);

        var lowerTokens = new string[tokens.Length];
        for (var i = 0; i < tokens.Length; i++)
            lowerTokens[i] = tokens[i].ToLowerInvariant();

        foreach (var (sectionName, section) in config.Sections)
        {
            if (!section.Enabled || section.Keywords.Count == 0)
                continue;

            foreach (var keyword in section.Keywords)
            {
                if (string.IsNullOrEmpty(keyword))
                    continue;

                var lowerKeyword = keyword.ToLowerInvariant();

                foreach (var token in lowerTokens)
                {
                    if (token.Equals(lowerKeyword, StringComparison.OrdinalIgnoreCase))
                    {
                        return new DynamicKeywordMatchResult
                        {
                            SectionName = sectionName,
                            MatchedKeyword = keyword,
                            CustomContent = section.CustomContent
                        };
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// 从配置构建词典（用于 FMM 切词）
    /// </summary>
    internal static HashSet<string> BuildDictionary(DynamicKeywordConfig config)
    {
        var dict = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var section in config.Sections.Values)
        {
            if (!section.Enabled)
                continue;

            foreach (var keyword in section.Keywords)
            {
                if (!string.IsNullOrEmpty(keyword))
                    dict.Add(keyword);
            }
        }

        return dict;
    }
}
