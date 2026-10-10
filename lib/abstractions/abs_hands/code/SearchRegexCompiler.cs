
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 搜索正则编译器 — 统一 RgEngine/SearchService 两处正则编译逻辑
/// <para>支持：FixedStrings(Regex.Escape)/WordRegexp(\b包裹)/SmartCase(模式全小写则忽略大小写)</para>
/// <para>引擎选型：优先 NonBacktracking(.NET 9+ DFA 线性时间 + AOT 兼容 + 无灾难性回溯)，
/// 不支持原子组等特性时 fallback 到解释器引擎(同样 AOT 兼容)。</para>
/// <para>正则超时 5s 防御灾难性回溯(fallback 引擎)。</para>
/// </summary>
public static class SearchRegexCompiler {
    private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 编译正则表达式
    /// </summary>
    /// <param name="pattern">正则模式</param>
    /// <param name="caseInsensitive">忽略大小写</param>
    /// <param name="multiline">多行模式（用 Singleline 让 . 匹配换行）</param>
    /// <param name="fixedStrings">字面量模式（Regex.Escape）</param>
    /// <param name="wordRegexp">词边界匹配（\b 包裹）</param>
    /// <param name="smartCase">智能大小写（模式全小写则忽略大小写）</param>
    /// <returns>(Regex, null) 成功; (null, errorMsg) 失败</returns>
    public static (Regex? Regex, string? Error) Compile(
        string pattern,
        bool caseInsensitive = false,
        bool multiline = false,
        bool fixedStrings = false,
        bool wordRegexp = false,
        bool smartCase = false) {
        var p = pattern;
        if (fixedStrings)
            p = Regex.Escape(p);
        if (wordRegexp)
            p = $@"\b(?:{p})\b";

        var options = RegexOptions.None;
        if (multiline)
            options |= RegexOptions.Singleline;

        var ignoreCase = caseInsensitive;
        if (smartCase && !p.Any(char.IsUpper))
            ignoreCase = true;
        if (ignoreCase)
            options |= RegexOptions.IgnoreCase;

        try {
            return (new Regex(p, options | RegexOptions.NonBacktracking, RegexTimeout), null);
        } catch (ArgumentException) {
            try {
                return (new Regex(p, options, RegexTimeout), null);
            } catch (ArgumentException ex) {
                return (null, ex.Message);
            }
        }
    }
}