


namespace JoinCode.CliNode.Web;

/// <summary>
/// HTML → Markdown 转换（web_to_markdown 的本地实现）。
/// <para>刻意不引入 HTML 解析库：工具 exe 只允许引用契约层，第三方依赖会破坏 81 DLL / 16MB 的体积基线。</para>
/// <para>覆盖标题 / 段落 / 列表 / 链接 / 图片 / 代码块 / 行内代码 / 粗斜体 / 引用 / 分隔线 / 表格行；
/// 其余标签按「丢弃标签保留文字」处理。转换结果是给人读的正文，不追求逐字节等价原 DOM。</para>
/// </summary>
internal static class HtmlMarkdown {
    /// <summary>标签匹配：属性值里的 &gt; 不会截断标签（引号内内容整体吃掉）。</summary>
    private static readonly Regex TagRegex = new(
        "<(/?)([a-zA-Z][a-zA-Z0-9:-]*)((?:[^>\"']|\"[^\"]*\"|'[^']*')*)>",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly Regex CommentRegex = new("<!--.*?-->", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>声明节点（&lt;!doctype&gt; / &lt;?xml&gt;）不是标签也不是正文，先剔除，否则会当纯文字漏进结果。</summary>
    private static readonly Regex DeclarationRegex = new(
        "<![^>]*>|<\\?.*?\\?>", RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex TitleRegex = new(
        "<title[^>]*>(.*?)</title>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);

    /// <summary>正文容器：优先 &lt;main&gt; / &lt;article&gt;，没有才退回整页，避免把导航栏当正文。</summary>
    private static readonly Regex MainRegex = new(
        "<(main|article)\\b[^>]*>(.*?)</\\1\\s*>", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.IgnoreCase);
    private static readonly Regex WhitespaceRegex = new("[ \\t\\f\\v]+", RegexOptions.Compiled);
    private static readonly Regex EntityRegex = new("&(#[xX]?[0-9a-fA-F]+|[a-zA-Z][a-zA-Z0-9]*);", RegexOptions.Compiled);

    private static readonly string[] BlockTags = [
        "p", "div", "section", "article", "header", "footer", "main", "aside", "nav",
        "figure", "figcaption", "address", "center", "form", "fieldset", "details", "summary"
    ];

    private static readonly Dictionary<string, string> Entities = new(StringComparer.OrdinalIgnoreCase) {
        ["amp"] = "&", ["lt"] = "<", ["gt"] = ">", ["quot"] = "\"", ["apos"] = "'",
        ["nbsp"] = " ", ["ensp"] = " ", ["emsp"] = " ", ["thinsp"] = " ",
        ["mdash"] = "—", ["ndash"] = "–", ["hellip"] = "…", ["middot"] = "·",
        ["copy"] = "©", ["reg"] = "®", ["trade"] = "™", ["deg"] = "°",
        ["laquo"] = "«", ["raquo"] = "»", ["ldquo"] = "“", ["rdquo"] = "”",
        ["lsquo"] = "‘", ["rsquo"] = "’", ["times"] = "×", ["divide"] = "÷",
        ["plusmn"] = "±", ["frac12"] = "½", ["frac14"] = "¼", ["frac34"] = "¾",
        ["sup2"] = "²", ["sup3"] = "³", ["euro"] = "€", ["pound"] = "£", ["yen"] = "¥",
        ["sect"] = "§", ["para"] = "¶", ["dagger"] = "†", ["bull"] = "•",
        ["rarr"] = "→", ["larr"] = "←", ["uarr"] = "↑", ["darr"] = "↓", ["harr"] = "↔",
        ["infin"] = "∞", ["ne"] = "≠", ["le"] = "≤", ["ge"] = "≥", ["asymp"] = "≈",
        ["sum"] = "∑", ["prod"] = "∏", ["radic"] = "√", ["int"] = "∫", ["part"] = "∂",
        ["exist"] = "∃", ["forall"] = "∀", ["empty"] = "∅", ["isin"] = "∈", ["notin"] = "∉",
        ["cap"] = "∩", ["cup"] = "∪", ["sim"] = "∼", ["equiv"] = "≡", ["sub"] = "⊂", ["supe"] = "⊃"
    };

    public static string Convert(string html) {
        if (string.IsNullOrWhiteSpace(html)) return string.Empty;

        var title = Decode(TitleRegex.Match(html).Groups[1].Value);
        var body = StripHidden(DeclarationRegex.Replace(
            CommentRegex.Replace(html.Replace("\r\n", "\n"), " "), " "));
        body = ExtractMain(body);

        var output = new StringBuilder(body.Length / 2);
        var anchors = new Stack<(int Marker, string Href)>();
        var inPre = 0;
        var cursor = 0;

        foreach (Match tag in TagRegex.Matches(body)) {
            if (tag.Index > cursor) AppendText(output, Decode(body[cursor..tag.Index]), inPre > 0);
            cursor = tag.Index + tag.Length;
            HandleTag(output, tag.Groups[2].Value.ToLowerInvariant(), tag.Groups[1].Value == "/",
                tag.Groups[3].Value, anchors, ref inPre);
        }
        if (cursor < body.Length) AppendText(output, Decode(body[cursor..]), inPre > 0);

        var text = Normalize(output.ToString());
        var heading = Decode(title).Trim();
        if (heading.Length == 0) return text;
        // 多数站点首段就是同名 H1，重复补标题只会让正文出现两个一样的标题。
        if (text.StartsWith("# " + heading, StringComparison.Ordinal)) return text;
        return $"# {heading}\n\n{text}";
    }

    /// <summary>单页站点常把正文放在 main/article；抽不到（或正文太短）才退回整页。</summary>
    private static string ExtractMain(string html) {
        var match = MainRegex.Match(html);
        if (!match.Success) return html;
        var inner = match.Groups[2].Value;
        return inner.Length > 500 ? inner : html;
    }

    /// <summary>逐字符跟踪隐藏元素深度：script/style 内部的 "&lt;p&gt;" 是源码，不能当正文。</summary>
    private static string StripHidden(string html) {
        var result = new StringBuilder(html.Length);
        var depth = 0;
        var cursor = 0;
        foreach (Match tag in TagRegex.Matches(html)) {
            var name = tag.Groups[2].Value.ToLowerInvariant();
            var closing = tag.Groups[1].Value == "/";
            var hidden = IsHiddenTag(name);

            if (depth == 0 && tag.Index > cursor) result.Append(html, cursor, tag.Index - cursor);
            cursor = tag.Index + tag.Length;

            if (!hidden) {
                if (depth == 0) result.Append(tag.Value);
                continue;
            }
            if (closing) {
                if (depth > 0) depth--;
            } else {
                depth++;
            }
        }
        if (depth == 0 && cursor < html.Length) result.Append(html, cursor, html.Length - cursor);
        return result.ToString();
    }

    private static bool IsHiddenTag(string name) => name is "script" or "style" or "noscript" or "svg"
        or "head" or "iframe" or "canvas" or "template" or "object" or "embed" or "select" or "textarea"
        or "nav" or "footer";

    private static void HandleTag(
        StringBuilder sb, string name, bool closing, string attrs, Stack<(int Marker, string Href)> anchors, ref int inPre) {
        switch (name) {
            case "h1" or "h2" or "h3" or "h4" or "h5" or "h6":
                if (closing) {
                    Blank(sb, 2);
                } else {
                    Blank(sb, 2);
                    sb.Append('#', name[1] - '0').Append(' ');
                }
                return;
            case "ul" or "ol" or "table" or "thead" or "tbody" or "tfoot" or "tr" or "figcaption":
                Blank(sb, name == "tr" && closing ? 1 : 2);
                if (name == "tr" && closing) sb.Append('|');
                return;
            case "li":
                if (closing) {
                    Blank(sb, 1);
                } else {
                    Blank(sb, 1);
                    sb.Append("- ");
                }
                return;
            case "blockquote":
                Blank(sb, 2);
                return;
            case "br":
                if (!closing) sb.Append('\n');
                return;
            case "hr":
                if (!closing) {
                    Blank(sb, 2);
                    sb.Append("---");
                    Blank(sb, 2);
                }
                return;
            case "pre":
                if (!closing) {
                    Blank(sb, 2);
                    sb.Append("```").Append('\n');
                    inPre++;
                } else {
                    if (inPre > 0) inPre--;
                    if (sb.Length > 0 && sb[^1] != '\n') sb.Append('\n');
                    sb.Append("```");
                    Blank(sb, 2);
                }
                return;
            case "code":
                if (inPre == 0) sb.Append('`');
                return;
            case "strong" or "b":
                sb.Append("**");
                return;
            case "em" or "i" or "cite" or "var":
                sb.Append('*');
                return;
            case "del" or "s" or "strike":
                sb.Append("~~");
                return;
            case "a":
                if (closing) {
                    if (anchors.Count == 0) return;
                    var (marker, href) = anchors.Pop();
                    // 图标链接（只有 <a> 没有文字）不产出 []()，否则正文里全是空方括号噪音。
                    if (sb.Length == marker + 1) { sb.Length = marker; return; }
                    sb.Append("](").Append(href).Append(')');
                } else {
                    var href = Attribute(attrs, "href");
                    if (string.IsNullOrWhiteSpace(href) || href.StartsWith('#')) return;
                    sb.Append('[');
                    anchors.Push((sb.Length - 1, href));
                }
                return;
            case "img":
                if (!closing) AppendImage(sb, attrs);
                return;
            case "td" or "th":
                if (closing) {
                    sb.Append(' ');
                } else {
                    sb.Append("| ");
                }
                return;
            case "input" or "button" or "meta" or "link" or "base" or "col" or "colgroup"
                or "source" or "track" or "param" or "wbr" or "area" or "map":
                return;
            default:
                if (BlockTags.Contains(name)) Blank(sb, 2);
                return;
        }
    }

    private static void AppendImage(StringBuilder sb, string attrs) {
        var src = Attribute(attrs, "src") ?? Attribute(attrs, "data-src");
        if (string.IsNullOrWhiteSpace(src)) return;
        var alt = Attribute(attrs, "alt") ?? string.Empty;
        sb.Append("![").Append(alt).Append("](").Append(src).Append(')');
    }

    private static string? Attribute(string attrs, string name) {
        var match = Regex.Match(
            attrs,
            "\\b" + Regex.Escape(name) + "\\s*=\\s*(?:\"([^\"]*)\"|'([^']*)'|([^\\s>]+))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return null;
        var value = match.Groups[1].Success ? match.Groups[1].Value
            : match.Groups[2].Success ? match.Groups[2].Value
            : match.Groups[3].Value;
        return Decode(value).Trim();
    }

    /// <summary>保证输出以 n 个换行结尾；sb 为空时不写前导空行。</summary>
    private static void Blank(StringBuilder sb, int count) {
        if (sb.Length == 0) return;
        var trailing = 0;
        for (var i = sb.Length - 1; i >= 0 && sb[i] == '\n'; i--) trailing++;
        for (var i = trailing; i < count; i++) sb.Append('\n');
    }

    private static void AppendText(StringBuilder sb, string text, bool inPre) {
        if (text.Length == 0) return;
        if (inPre) {
            sb.Append(text);
            return;
        }
        var collapsed = WhitespaceRegex.Replace(text, " ");
        if (collapsed.Length == 0) return;
        if (sb.Length == 0 || sb[^1] == '\n' || sb[^1] == ' ') collapsed = collapsed.TrimStart();
        if (collapsed.Length == 0) return;
        sb.Append(collapsed);
    }

    private static string Decode(string text) {
        if (text.IndexOf('&') < 0) return text;
        return EntityRegex.Replace(text, match => {
            var token = match.Groups[1].Value;
            if (token.StartsWith('#')) return DecodeNumeric(token);
            return Entities.TryGetValue(token, out var value) ? value : match.Value;
        });
    }

    private static string DecodeNumeric(string token) {
        var hex = token.Length > 1 && (token[1] is 'x' or 'X');
        var digits = hex ? token[2..] : token[1..];
        var parsed = hex
            ? int.TryParse(digits, System.Globalization.NumberStyles.HexNumber, null, out var code)
            : int.TryParse(digits, out code);
        if (!parsed || code <= 0 || code > 0x10FFFF) return "&#" + token + ";";
        return char.ConvertFromUtf32(code);
    }

    /// <summary>行尾去空白、连续空行折叠成一条，整体剪掉首尾空行。</summary>
    private static string Normalize(string text) {
        var lines = text.Split('\n');
        var output = new List<string>(lines.Length);
        foreach (var raw in lines) {
            var line = raw.TrimEnd();
            if (line.Length == 0) {
                if (output.Count == 0) continue;
                if (output.Count >= 1 && output[^1].Length == 0) continue;
            }
            output.Add(line);
        }
        while (output.Count > 0 && output[^1].Length == 0) output.RemoveAt(output.Count - 1);
        return string.Join('\n', output);
    }
}
