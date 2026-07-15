using TreeSitter;

namespace JoinCode.Abstractions.Security.Shell;

public sealed partial class BashAstSecurityWalker
{
    private static VarAssignmentOrTooComplex WalkVariableAssignment(
        Node node, List<BashSimpleCommandInfo> innerCommands, Dictionary<string, string> varScope)
    {
        string? name = null;
        var value = "";
        var isAppend = false;

        foreach (var child in node.Children)
        {
            if (child is null) continue;

            switch (child.Type)
            {
                case "variable_name":
                    name = child.Text;
                    break;

                case "=":
                    isAppend = false;
                    break;

                case "+=":
                    isAppend = true;
                    break;

                case "command_substitution":
                {
                    var innerScope = new Dictionary<string, string>(varScope);
                    var err = CollectCommandSubstitution(child, innerCommands, innerScope);
                    if (err is not null) return new VarAssignmentOrTooComplex(err);
                    value = CmdsubPlaceholder;
                    break;
                }

                case "simple_expansion":
                {
                    var v = ResolveSimpleExpansion(child, varScope, insideString: true);
                    if (v.IsTooComplex)
                        return new VarAssignmentOrTooComplex(v.GetTooComplex());
                    value = v.Value;
                    break;
                }

                default:
                {
                    var arg = WalkArgument(child, innerCommands, varScope);
                    if (arg.IsTooComplex)
                        return new VarAssignmentOrTooComplex(arg.GetTooComplex());
                    value = arg.Value;
                    break;
                }
            }
        }

        if (name is null)
        {
            return new VarAssignmentOrTooComplex(new BashAstSecurityResult.TooComplex(
                "Variable assignment without name", "VAR_ASSIGN_NO_NAME"));
        }

        if (!IsValidVarName(name))
        {
            return new VarAssignmentOrTooComplex(new BashAstSecurityResult.TooComplex(
                $"Invalid variable name (bash treats as command): {name}", "INVALID_VAR_NAME"));
        }

        if (name.Equals("IFS", StringComparison.OrdinalIgnoreCase))
        {
            return new VarAssignmentOrTooComplex(new BashAstSecurityResult.TooComplex(
                "IFS assignment changes word-splitting — cannot model statically", "IFS_ASSIGNMENT"));
        }

        if (name.Equals("PS4", StringComparison.OrdinalIgnoreCase))
        {
            if (isAppend)
            {
                return new VarAssignmentOrTooComplex(new BashAstSecurityResult.TooComplex(
                    "PS4 += cannot be statically verified — combine into a single PS4= assignment", "PS4_APPEND"));
            }
            if (ContainsAnyPlaceholder(value))
            {
                return new VarAssignmentOrTooComplex(new BashAstSecurityResult.TooComplex(
                    "PS4 value derived from cmdsub/variable — runtime unknowable", "PS4_PLACEHOLDER"));
            }
            if (!IsPs4ValueSafe(value))
            {
                return new VarAssignmentOrTooComplex(new BashAstSecurityResult.TooComplex(
                    "PS4 value outside safe charset — only ${VAR} refs and [A-Za-z0-9 _+:.=/[]-] allowed", "PS4_UNSAFE_CHARSET"));
            }
        }

        if (value.Contains('~'))
        {
            return new VarAssignmentOrTooComplex(new BashAstSecurityResult.TooComplex(
                "Tilde in assignment value — bash may expand at assignment time", "TILDE_IN_ASSIGNMENT"));
        }

        return new VarAssignmentOrTooComplex(new VarAssignmentResult(name, value, isAppend));
    }

    private static BashAstSecurityResult? WalkStandaloneVariableAssignment(
        Node node, Dictionary<string, string> varScope)
    {
        var innerCommands = new List<BashSimpleCommandInfo>();
        var ev = WalkVariableAssignment(node, innerCommands, varScope);
        if (ev.IsTooComplex) return ev.TooComplex;

        ApplyVarToScope(varScope, ev.GetResult());
        return null;
    }

    private static void ApplyVarToScope(Dictionary<string, string> varScope, VarAssignmentResult ev)
    {
        var existing = varScope.TryGetValue(ev.Name, out var v) ? v : "";
        var combined = ev.IsAppend ? existing + ev.Value : ev.Value;
        varScope[ev.Name] = ContainsAnyPlaceholder(combined) ? VarPlaceholder : combined;
    }

    private static StringOrTooComplex ResolveSimpleExpansion(
        Node node, Dictionary<string, string> varScope, bool insideString)
    {
        string? varName = null;
        var isSpecial = false;

        foreach (var child in node.Children)
        {
            if (child is null) continue;
            if (child.Type == "variable_name")
            {
                varName = child.Text;
                break;
            }
            if (child.Type == "special_variable_name")
            {
                varName = child.Text;
                isSpecial = true;
                break;
            }
        }

        if (varName is null)
            return new StringOrTooComplex(TooComplexNode(node));

        if (varScope.TryGetValue(varName, out var trackedValue))
        {
            if (ContainsAnyPlaceholder(trackedValue))
            {
                if (!insideString)
                    return new StringOrTooComplex(TooComplexNode(node));
                return new StringOrTooComplex(VarPlaceholder);
            }

            if (!insideString)
            {
                if (trackedValue.Length == 0)
                    return new StringOrTooComplex(TooComplexNode(node));
                if (BareVarUnsafeRegex().IsMatch(trackedValue))
                    return new StringOrTooComplex(TooComplexNode(node));
            }
            return new StringOrTooComplex(trackedValue);
        }

        if (insideString)
        {
            if (SafeEnvVars.Contains(varName))
                return new StringOrTooComplex(VarPlaceholder);
            if (isSpecial && (SpecialVarNames.Contains(varName) || DigitsOnlyRegex().IsMatch(varName)))
                return new StringOrTooComplex(VarPlaceholder);
        }

        return new StringOrTooComplex(TooComplexNode(node)!);
    }

    private static BashAstSecurityResult? WalkDeclarationCommand(
        Node node, List<BashSimpleCommandInfo> commands, Dictionary<string, string> varScope)
    {
        var cmdName = "";
        foreach (var child in node.Children)
        {
            if (child is null) continue;
            if (child.Type == "command_name")
            {
                cmdName = child.Text;
                break;
            }
        }

        foreach (var child in node.Children)
        {
            if (child is null) continue;
            if (child.Type == "variable_assignment")
            {
                var ev = WalkVariableAssignment(child, commands, varScope);
                if (ev.IsTooComplex) return ev.TooComplex;
                ApplyVarToScope(varScope, ev.GetResult());
            }
        }

        var argv = new List<string>();
        foreach (var child in node.Children)
        {
            if (child is null) continue;
            switch (child.Type)
            {
                case "command_name":
                    argv.Add(child.Text);
                    break;
                case "word":
                case "number":
                    argv.Add(child.Text);
                    break;
                case "variable_assignment":
                    break;
                case "simple_expansion":
                {
                    var v = ResolveSimpleExpansion(child, varScope, insideString: false);
                    if (v.IsTooComplex) return v.TooComplex;
                    argv.Add(v.Value);
                    break;
                }
                default:
                    return TooComplexNode(child);
            }
        }

        if (argv.Count > 0)
            commands.Add(new BashSimpleCommandInfo([.. argv], [], [], node.Text));

        return null;
    }
}
