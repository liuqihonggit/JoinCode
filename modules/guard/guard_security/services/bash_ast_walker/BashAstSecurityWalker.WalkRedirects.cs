namespace JoinCode.Abstractions.Security.Shell;

public sealed partial class BashAstSecurityWalker {
    private static BashAstSecurityResult? WalkHeredocRedirect(Node node) {
        // 对齐 TS walkHeredocRedirect:通过 heredoc_start 节点判断分隔符是否引号包裹。
        // 仅引号分隔符 heredoc(<<'EOF')安全 — body 为字面量,不经历展开。
        string? startText = null;
        Node? body = null;

        foreach (var child in node.Children) {
            if (child is null) continue;
            if (child.Type == "heredoc_start") {
                startText = child.Text;
            } else if (child.Type == "heredoc_body") {
                body = child;
            } else if (child.Type is "<<" or "<<-" or "heredoc_end" or "file_descriptor") {
                // 结构 token — 跳过
            } else {
                // 管道/命令等跟随分隔符的节点 — fail closed
                return TooComplexNode(child);
            }
        }

        var isQuoted = startText is not null && (
            (startText.StartsWith('\'') && startText.EndsWith('\'')) ||
            (startText.StartsWith('"') && startText.EndsWith('"')) ||
            startText.StartsWith('\\'));

        if (!isQuoted) {
            return new BashAstSecurityResult.TooComplex(
                "Heredoc with unquoted delimiter undergoes shell expansion", "heredoc_redirect");
        }

        if (body is not null) {
            foreach (var child in body.Children) {
                if (child is null) continue;
                if (child.Type != "heredoc_content") {
                    return TooComplexNode(child);
                }
            }
        }

        return null;
    }

    private static BashAstSecurityResult? WalkHerestringRedirect(
        Node node, List<BashSimpleCommandInfo> innerCommands, Dictionary<string, string> varScope) {
        foreach (var child in node.Children) {
            if (child is null) continue;

            switch (child.Type) {
                case "<<<":
                continue;

                case "word":
                case "raw_string":
                case "string":
                case "simple_expansion":
                case "command_substitution":
                case "concatenation": {
                    if (child.Type == "command_substitution") {
                        var innerScope = new Dictionary<string, string>(varScope);
                        var err = CollectCommandSubstitution(child, innerCommands, innerScope);
                        if (err is not null) return err;
                    } else if (child.Type == "string") {
                        var result = WalkString(child, innerCommands, varScope);
                        if (result.IsTooComplex) return result.TooComplex;
                    } else if (child.Type == "simple_expansion") {
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
        Node node, List<BashSimpleCommandInfo> innerCommands, Dictionary<string, string> varScope) {
        var op = "";
        var target = "";

        foreach (var child in node.Children) {
            if (child is null) continue;

            switch (child.Type) {
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
                case "string": {
                    var result = WalkString(child, innerCommands, varScope);
                    if (result.IsTooComplex)
                        return new RedirectOrTooComplex(result.GetTooComplex());
                    target = result.Value;
                    break;
                }
                case "simple_expansion": {
                    var v = ResolveSimpleExpansion(child, varScope, insideString: false);
                    if (v.IsTooComplex)
                        return new RedirectOrTooComplex(v.GetTooComplex());
                    target = v.Value;
                    break;
                }
                case "command_substitution":
                return new RedirectOrTooComplex(new BashAstSecurityResult.TooComplex(
                    "重定向目标包含命令替换", "CMDSUB_REDIRECT"));
                default:
                return new RedirectOrTooComplex(TooComplexNode(child));
            }
        }

        if (string.IsNullOrEmpty(op))
            return new RedirectOrTooComplex(new BashAstSecurityResult.TooComplex(
                "重定向缺少操作符", "MISSING_REDIRECT_OP"));

        return new RedirectOrTooComplex(new RedirectResult(op, target));
    }

    private static BashAstSecurityResult? CollectCommandSubstitution(
        Node node, List<BashSimpleCommandInfo> innerCommands, Dictionary<string, string> varScope) {
        // 对齐 TS collectCommandSubstitution:跳过 $( ` ) 结构 token,
        // 仅递归处理内部语句。
        foreach (var child in node.Children) {
            if (child is null) continue;
            if (child.Type is "$(" or "`" or ")") continue;
            var err = CollectCommands(child, innerCommands, varScope);
            if (err is not null) return err;
        }
        return null;
    }
}