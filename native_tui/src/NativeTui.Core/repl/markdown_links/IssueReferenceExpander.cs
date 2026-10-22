namespace NativeTui.Core.Repl;

/// <summary>
/// issue 引用扫描器 — 对齐基准 utils/markdown.ts:289-308 linkifyIssueReferences（纯数据层拆分）：
/// 产出文本区间与 GitHub URL，文本替换/渲染消费归集成层（基准 replace 内联在渲染链，本项目拆区间数据）。
/// 完整形态 owner/repo#NNN（基准唯一形态 — owner 段禁点防 docs.github.io/guide#42 类域名误报，
/// repo 段许点如 cc.kurs.web）；短形态 #NNN 需 defaultOwnerRepo 上下文（基准已删除裸形态因
/// 猜测当前仓库常猜错 — 本项目恢复为显式上下文才展开，无上下文保守跳过）。
/// 前缀类 (^|[^\w./-]) 排除 \w / . / - — 防 URL 路径与已消费字符误报；短形态与完整形态
/// 天然互斥（完整形态 # 前必为 repo 尾字符 ∈ [\w.-]，均被短形态前缀排除 — 无需重叠过滤）。
/// 正则均 ECMAScript 选项 — \w/\b 收敛 ASCII 语义对齐 JS 基准（C# 默认 Unicode \w 会吞中文边界）。
/// </summary>
public static partial class IssueReferenceExpander
{
    /// <summary>完整形态 owner/repo#NNN（基准 ISSUE_REF_PATTERN 同构 — 前缀组不捕获，区间由 repo/num 组定位）。</summary>
    [GeneratedRegex(@"(?:^|[^\w./-])(?<repo>[A-Za-z0-9][\w-]*/[A-Za-z0-9][\w.-]*)#(?<num>\d+)\b", RegexOptions.ECMAScript)]
    private static partial Regex QualifiedRefRegex();

    /// <summary>短形态 #NNN（前缀排除 [\w./-] — 防 URL 锚点/路径与完整形态重叠误报）。</summary>
    [GeneratedRegex(@"(?:^|[^\w./-])#(?<num>\d+)\b", RegexOptions.ECMAScript)]
    private static partial Regex BareRefRegex();

    /// <summary>defaultOwnerRepo 格式校验（owner/repo 段字符 ∈ [\w.-] — 防配置错误产出非法 URL）。</summary>
    [GeneratedRegex(@"^[\w.-]+/[\w.-]+$", RegexOptions.ECMAScript)]
    private static partial Regex OwnerRepoFormatRegex();

    /// <summary>
    /// 扫描文本中的 issue 引用 — 完整形态始终识别；短形态仅当 defaultOwnerRepo 为合法
    /// owner/repo 时识别（null/空白/格式非法跳过 — 基准删除裸形态语义的保守等价物）。
    /// 输出按区间起点文本序（完整与短形态混排时归并有序）。
    /// </summary>
    /// <param name="text">待扫描文本（多行 — 换行符作普通前缀字符参与匹配，^ 仅匹配串首对齐基准）。</param>
    /// <param name="defaultOwnerRepo">短形态归属仓库（"owner/repo" 如 "anthropics/claude-code"）；null/空白/格式非法时短形态不展开。</param>
    /// <returns>(Span, Url) 序列 — Span 为引用在 text 内的区间（不含前缀字符，Range 索引器可直接解引用），Url 为 https://github.com/{owner}/{repo}/issues/{n}。</returns>
    public static IEnumerable<(Range Span, string Url)> Expand(string text, string? defaultOwnerRepo = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        return ExpandQualified(text)
            .Concat(ExpandBareIfAvailable(text, defaultOwnerRepo))
            .OrderBy(reference => reference.Span.Start.Value);
    }

    /// <summary>完整形态扫描 — 区间 [repo 组起点, num 组终点)（不含前缀字符）。</summary>
    private static IEnumerable<(Range Span, string Url)> ExpandQualified(string text) =>
        QualifiedRefRegex().Matches(text).Select(QualifiedReference);

    /// <summary>短形态扫描 — 上下文合法才遍历，否则空序列（保守跳过）。</summary>
    private static IEnumerable<(Range Span, string Url)> ExpandBareIfAvailable(string text, string? defaultOwnerRepo) =>
        defaultOwnerRepo is not { } ownerRepo || !OwnerRepoFormatRegex().IsMatch(ownerRepo)
            ? []
            : BareRefRegex().Matches(text).Select(match => BareReference(match, ownerRepo));

    /// <summary>完整形态匹配 → 区间与 URL（区间由 repo/num 组坐标组装）。</summary>
    private static (Range Span, string Url) QualifiedReference(Match match)
    {
        var repo = match.Groups["repo"];
        var num = match.Groups["num"];
        return (new Range(repo.Index, num.Index + num.Length), $"https://github.com/{repo.Value}/issues/{num.Value}");
    }

    /// <summary>短形态匹配 → 区间与 URL（# 位置 = num 组起点 - 1 — ^ 零宽前缀下同样成立）。</summary>
    private static (Range Span, string Url) BareReference(Match match, string ownerRepo)
    {
        var num = match.Groups["num"];
        return (new Range(num.Index - 1, num.Index + num.Length), $"https://github.com/{ownerRepo}/issues/{num.Value}");
    }
}
