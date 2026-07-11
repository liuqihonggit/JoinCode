using TreeSitter;

namespace JoinCode.Abstractions.Security.Shell;

public sealed partial class BashAstSecurityWalker
{
    private static BashAstSecurityResult? WalkHeredocRedirect(Node node)
    {
        var hasQuotedDelimiter = false;

        foreach (var child in node.Children)
        {
            if (child is null) continue;

            if (child.Type == "heredoc_beginning" || child.Type == "word")
            {
                var text = child.Text;
                if (text.Contains('\'') || text.Contains('"'))
                {
                    hasQuotedDelimiter = true;
                }
            }

            if (child.Type == "heredoc_content" || child.Type == "heredoc_body")
            {
                if (!hasQuotedDelimiter)
                {
                    return new BashAstSecurityResult.TooComplex(
                        "非引号分隔符heredoc — body经历变量/命令替换展开", "UNQUOTED_HEREDOC");
                }
            }
        }

        return null;
    }

    private static BashAstSecurityResult? WalkHerestringRedirect(
        Node node, List<BashSimpleCommandInfo> innerCommands, Dictionary<string, string> varScope)
    {
        foreach (var child in node.Children)
        {
            if (child is null) continue;

            switch (child.Type)
            {
                case "<<<":
                    continue;

                case "word":
                case "raw_string":
                case "string":
                case "simple_expansion":
                case "command_substitution":
                case "concatenation":
                {
                    if (child.Type == "command_substitution")
                    {
                        var innerScope = new Dictionary<string, string>(varScope);
                        var err = CollectCommandSubstitution(child, innerCommands, innerScope);
                        if (err is not null) return err;
                    }
                    else if (child.Type == "string")
                    {
                        var result = WalkString(child, innerCommands, varScope);
                        if (result.IsTooComplex) return result.TooComplex;
                    }
                    else if (child.Type == "simple_expansion")
                    {
                        var v = ResolveSimpleExpansion(child, varScope, insideString: true);
                        if (v.IsTooComplex) return v.TooComplex;
                    }
                    break;
                }

                default:
                    return TooComplexNode(child);
            }
        }

        return null;
    }

    private static RedirectOrTooComplex WalkFileRedirect(
        Node node, List<BashSimpleCommandInfo> innerCommands, Dictionary<string, string> varScope)
    {
        var op = "";
        var target = "";

        foreach (var child in node.Children)
        {
            if (child is null) continue;

            switch (child.Type)
            {
                case "file_descriptor":
                    break;
                case ">":
                case ">>":
                case "<":
                case "&>":
                case "&>>":
                case ">|":
                case "<&":
                case ">&":
                    op = child.Type;
                    break;
                case "word":
                case "raw_string":
                    target = child.Type == "raw_string" ? StripRawString(child.Text) : child.Text;
                    break;
                case "string":
                {
                    var result = WalkString(child, innerCommands, varScope);
                    if (result.IsTooComplex)
                        return new RedirectOrTooComplex(result.TooComplex!);
                    target = result.Value;
                    break;
                }
                case "simple_expansion":
                {
                    var v = ResolveSimpleExpansion(child, varScope, insideString: false);
                    if (v.IsTooComplex)
                        return new RedirectOrTooComplex(v.TooComplex!);
                    target = v.Value;
                    break;
                }
                case "command_substitution":
                    return new RedirectOrTooComplex(new BashAstSecurityResult.TooComplex(
                        "重定向目标包含命令替换", "CMDSUB_REDIRECT"));
                default:
                    return new RedirectOrTooComplex(TooComplexNode(child)!);
            }
        }

        if (string.IsNullOrEmpty(op))
            return new RedirectOrTooComplex(new BashAstSecurityResult.TooComplex(
                "重定向缺少操作符", "MISSING_REDIRECT_OP"));

        return new RedirectOrTooComplex(new RedirectResult(op, target));
    }

    private static BashAstSecurityResult? CollectCommandSubstitution(
        Node node, List<BashSimpleCommandInfo> innerCommands, Dictionary<string, string> varScope)
    {
        foreach (var child in node.Children)
        {
            if (child is null) continue;
            var err = CollectCommands(child, innerCommands, varScope);
            if (err is not null) return err;
        }
        return null;
    }
}
