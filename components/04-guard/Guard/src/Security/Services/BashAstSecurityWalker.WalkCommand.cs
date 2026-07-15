using TreeSitter;

namespace JoinCode.Abstractions.Security.Shell;

public sealed partial class BashAstSecurityWalker
{
    private static BashAstSecurityResult? WalkCommand(
        Node node,
        BashRedirectInfo[] extraRedirects,
        List<BashSimpleCommandInfo> innerCommands,
        Dictionary<string, string> varScope)
    {
        var argv = new List<string>();
        var envVars = new List<BashEnvVarInfo>();
        var redirects = new List<BashRedirectInfo>();
        if (extraRedirects.Length > 0)
            redirects.AddRange(extraRedirects);

        foreach (var child in node.Children)
        {
            if (child is null) continue;

            switch (child.Type)
            {
                case "variable_assignment":
                {
                    var ev = WalkVariableAssignment(child, innerCommands, varScope);
                    if (ev.IsTooComplex) return ev.TooComplex;
                    var evResult = ev.GetResult();
                    envVars.Add(new BashEnvVarInfo(evResult.Name, evResult.Value));
                    break;
                }
                case "command_name":
                {
                    var arg = WalkArgument(child.Children.Count > 0 ? child.Children[0] ?? child : child,
                        innerCommands, varScope);
                    if (arg.IsTooComplex) return arg.TooComplex;
                    argv.Add(arg.Value);
                    break;
                }
                case "word":
                case "number":
                case "raw_string":
                case "string":
                case "concatenation":
                case "arithmetic_expansion":
                {
                    var arg = WalkArgument(child, innerCommands, varScope);
                    if (arg.IsTooComplex) return arg.TooComplex;
                    argv.Add(arg.Value);
                    break;
                }
                case "simple_expansion":
                {
                    var v = ResolveSimpleExpansion(child, varScope, insideString: false);
                    if (v.IsTooComplex) return v.TooComplex;
                    argv.Add(v.Value);
                    break;
                }
                case "file_redirect":
                {
                    var r = WalkFileRedirect(child, innerCommands, varScope);
                    if (r.IsTooComplex) return r.TooComplex;
                    var rr = r.GetResult();
                    redirects.Add(new BashRedirectInfo(rr.Op, rr.Target));
                    break;
                }
                case "heredoc_redirect":
                {
                    var err = WalkHeredocRedirect(child);
                    if (err is not null) return err;
                    break;
                }
                case "herestring_redirect":
                {
                    var err = WalkHerestringRedirect(child, innerCommands, varScope);
                    if (err is not null) return err;
                    break;
                }
                default:
                    return TooComplexNode(child);
            }
        }

        var text = node.Text;
        if (argv.Count > 0 && (text.Contains("$", StringComparison.Ordinal) || text.Contains('\n')))
        {
            text = string.Join(' ', argv);
        }

        if (argv.Count == 0)
            return null;

        innerCommands.Add(new BashSimpleCommandInfo([.. argv], [.. envVars], [.. redirects], text));
        return null;
    }

    private static BashAstSecurityResult? WalkRedirectedStatement(
        Node node, List<BashSimpleCommandInfo> commands, Dictionary<string, string> varScope)
    {
        var redirects = new List<BashRedirectInfo>();
        Node? innerCommand = null;

        foreach (var child in node.Children)
        {
            if (child is null) continue;

            if (child.Type == "file_redirect")
            {
                var r = WalkFileRedirect(child, commands, varScope);
                if (r.IsTooComplex) return r.TooComplex;
                var rr2 = r.GetResult();
                redirects.Add(new BashRedirectInfo(rr2.Op, rr2.Target));
            }
            else if (child.Type == "heredoc_redirect")
            {
                var err = WalkHeredocRedirect(child);
                if (err is not null) return err;
            }
            else if (child.Type is "command" or "pipeline" or "list"
                     or "negated_command" or "declaration_command" or "unset_command")
            {
                innerCommand = child;
            }
            else if (child.Type is "herestring_redirect")
            {
                var err = WalkHerestringRedirect(child, commands, varScope);
                if (err is not null) return err;
            }
            else
            {
                return TooComplexNode(child);
            }
        }

        if (innerCommand is null)
        {
            if (redirects.Count > 0)
                commands.Add(new BashSimpleCommandInfo([], [], [.. redirects], node.Text));
            return null;
        }

        var before = commands.Count;
        var err2 = CollectCommands(innerCommand, commands, varScope);
        if (err2 is not null) return err2;

        if (commands.Count > before && redirects.Count > 0)
        {
            var last = commands[^1];
            var merged = last.Redirects.ToList();
            merged.AddRange(redirects);
            commands[^1] = last with { Redirects = [.. merged] };
        }

        return null;
    }
}
