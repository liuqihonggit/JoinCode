namespace NativeTui.Core.Markdown;

/// <summary>
/// issue 引用链接化文档级展开（DSG043 尾批）— 消费 <see cref="IssueReferenceExpander"/>
/// 扫描结果把段落/标题/引用内的纯文本段拆为前文/引用子串（Link 样式 + GitHub Url）/后文
///（多实例循环拆分，前后文保留原样式）。已是链接段（Url 非空）跳过防嵌套破坏；
/// 代码块/表格不动（代码语义不链接化）。无可展开内容返回原文档实例（零分配）。
/// owner/repo 上下文由 git remote 解析（<see cref="GitRemoteResolver"/>），null 时短形态
/// #NNN 不展开（Expander 保守契约 — 基准删除裸形态猜库语义的等价物）。
/// </summary>
public static class IssueLinkExpander
{
    /// <summary>文档级展开 — 遍历块递归（段落/标题段扫描拆分，引用递归子块；无命中返回原实例）。</summary>
    /// <param name="document">原始文档（不修改 — 命中时产新实例）。</param>
    /// <param name="defaultOwnerRepo">短形态上下文（"owner/repo"；null = 短形态不展开）。</param>
    /// <returns>展开后文档（无命中 same instance 零分配）。</returns>
    public static MarkdownDocument ExpandDocument(MarkdownDocument document, string? defaultOwnerRepo) =>
        document.Blocks.Any(block => HasExpandableBlock(block, defaultOwnerRepo))
            ? document with { Blocks = [.. document.Blocks.Select(block => ExpandBlock(block, defaultOwnerRepo))] }
            : document;

    /// <summary>块级展开分发 — 段落/标题段扫描；引用递归子块；其余原样。</summary>
    private static MarkdownBlock ExpandBlock(MarkdownBlock block, string? defaultOwnerRepo) => block switch
    {
        MarkdownParagraph paragraph => ExpandParagraph(paragraph, defaultOwnerRepo),
        MarkdownHeading heading => new MarkdownHeading(heading.Level, ExpandSegments(heading.Segments, defaultOwnerRepo)),
        MarkdownQuote quote => new MarkdownQuote([.. quote.Children.Select(child => ExpandBlock(child, defaultOwnerRepo))]),
        _ => block,
    };

    /// <summary>段落展开（段命中才产新实例）。</summary>
    private static MarkdownParagraph ExpandParagraph(MarkdownParagraph paragraph, string? defaultOwnerRepo)
    {
        var expanded = ExpandSegments(paragraph.Segments, defaultOwnerRepo);
        return ReferenceEquals(expanded, paragraph.Segments) ? paragraph : new MarkdownParagraph(expanded);
    }

    /// <summary>是否有可展开块（引用递归判定 — 文档级零分配路径的命中预检）。</summary>
    private static bool HasExpandableBlock(MarkdownBlock block, string? defaultOwnerRepo) => block switch
    {
        MarkdownParagraph paragraph => paragraph.Segments.Any(s => IsExpandable(s, defaultOwnerRepo)),
        MarkdownHeading heading => heading.Segments.Any(s => IsExpandable(s, defaultOwnerRepo)),
        MarkdownQuote quote => quote.Children.Any(child => HasExpandableBlock(child, defaultOwnerRepo)),
        _ => false,
    };

    /// <summary>段可展开判定 — 非链接纯文本段且扫描命中至少一实例。</summary>
    private static bool IsExpandable(MarkdownSegment segment, string? defaultOwnerRepo) =>
        segment.Url is null && IssueReferenceExpander.Expand(segment.Text, defaultOwnerRepo).Any();

    /// <summary>段序列展开（命中段拆分，未命中段原引用保留 — 序列级零命中返回原序列实例）。</summary>
    private static IReadOnlyList<MarkdownSegment> ExpandSegments(IReadOnlyList<MarkdownSegment> segments, string? defaultOwnerRepo)
    {
        var changed = false;
        var result = new List<MarkdownSegment>(segments.Count);
        foreach (var segment in segments)
        {
            if (IsExpandable(segment, defaultOwnerRepo))
            {
                changed = true;
                result.AddRange(ExpandOneSegment(segment, defaultOwnerRepo));
            }
            else
            {
                result.Add(segment);
            }
        }

        return changed ? result : segments;
    }

    /// <summary>单段拆分 — 扫描实例区间倒序切（索引不位移），每实例产前文/引用/后文；
    /// 前后文保留原样式，引用子串叠 Link 样式 + Url；相邻实例间前文可能为空串段（跳过不产）。</summary>
    private static IEnumerable<MarkdownSegment> ExpandOneSegment(MarkdownSegment segment, string? defaultOwnerRepo)
    {
        var text = segment.Text;
        var matches = IssueReferenceExpander.Expand(text, defaultOwnerRepo).ToList();
        var cursor = 0;
        foreach (var (span, url) in matches)
        {
            var (start, length) = span.GetOffsetAndLength(text.Length);
            if (start > cursor)
            {
                yield return segment with { Text = text[cursor..start], Url = null };
            }

            yield return segment with { Text = text.Substring(start, length), Style = segment.Style | MarkdownStyle.Link, Url = url };
            cursor = start + length;
        }

        if (cursor < text.Length)
        {
            yield return segment with { Text = text[cursor..], Url = null };
        }
    }
}

/// <summary>
/// git remote URL → GitHub owner/repo 解析（DSG043 尾批）— ssh（git@github.com:o/r[.git]）与
/// https（https://github.com/o/r[.git]）两形态；非 GitHub/形态不完整返回 null（短形态不展开）。
/// 纯函数（remote 获取由宿主进程调用 — GitRemoteResolver.ResolveAsync）。
/// </summary>
public static class GitRemoteResolver
{
    /// <summary>解析 remote URL → "owner/repo"（null = 非 GitHub 或形态不完整）。</summary>
    public static string? ParseGitHub(string remoteUrl)
    {
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            return null;
        }

        var trimmed = remoteUrl.Trim();
        string? path = null;
        if (trimmed.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
        {
            path = trimmed["git@github.com:".Length..];
        }
        else if (trimmed.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
        {
            path = trimmed["https://github.com/".Length..];
        }

        if (path is null)
        {
            return null;
        }

        if (path.EndsWith(".git", StringComparison.Ordinal))
        {
            path = path[..^4];
        }

        var slash = path.IndexOf('/');
        return slash > 0 && slash < path.Length - 1 && path.IndexOf('/', slash + 1) < 0
            ? path
            : null;
    }
}
