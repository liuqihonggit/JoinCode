
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 搜索正则编译器 — 统一 RgEngine/SearchService 两处正则编译逻辑
/// <para>支持：FixedStrings(Regex.Escape)/WordRegexp(\b包裹)/SmartCase(模式全小写则忽略大小写)</para>
/// </summary>
public static class SearchRegexCompiler
{
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
        bool smartCase = false)
    {
        var p = pattern;
        if (fixedStrings)
            p = Regex.Escape(p);
        if (wordRegexp)
            p = $@"\b(?:{p})\b";

        var options = RegexOptions.Compiled;
        if (multiline)
            options |= RegexOptions.Singleline;

        var ignoreCase = caseInsensitive;
        if (smartCase && !p.Any(char.IsUpper))
            ignoreCase = true;
        if (ignoreCase)
            options |= RegexOptions.IgnoreCase;

        try
        {
            return (new Regex(p, options), null);
        }
        catch (ArgumentException ex)
        {
            return (null, ex.Message);
        }
    }
}
