using TreeSitter;

namespace JoinCode.Abstractions.Security.Shell;

public sealed partial class BashAstSecurityWalker
{
    private static StringOrTooComplex WalkArgument(
        Node node, List<BashSimpleCommandInfo> innerCommands, Dictionary<string, string> varScope)
    {
        if (node is null)
            return new StringOrTooComplex(new BashAstSecurityResult.TooComplex("Null argument node", "NULL_ARG"));

        switch (node.Type)
        {
            case "word":
            {
                if (BraceExpansionRegex.IsMatch(node.Text))
                    return new StringOrTooComplex(TooComplexNode(node)!);
                var text = BackslashUnescapeRegex().Replace(node.Text, "$1");
                return new StringOrTooComplex(text);
            }

            case "number":
            {
                if (node.Children.Count > 0)
                    return new StringOrTooComplex(TooComplexNode(node)!);
                return new StringOrTooComplex(node.Text);
            }

            case "raw_string":
                return new StringOrTooComplex(StripRawString(node.Text));

            case "string":
                return WalkString(node, innerCommands, varScope);

            case "concatenation":
            {
                if (BraceExpansionRegex.IsMatch(node.Text))
                    return new StringOrTooComplex(TooComplexNode(node)!);
                var result = new StringBuilder();
                foreach (var child in node.Children)
                {
                    if (child is null) continue;
                    var part = WalkArgument(child, innerCommands, varScope);
                    if (part.IsTooComplex) return part;
                    result.Append(part.Value);
                }
                return new StringOrTooComplex(result.ToString());
            }

            case "arithmetic_expansion":
            {
                var err = WalkArithmetic(node);
                if (err is not null) return new StringOrTooComplex(err);
                return new StringOrTooComplex(node.Text);
            }

            case "simple_expansion":
                return ResolveSimpleExpansion(node, varScope, insideString: false);

            default:
                return new StringOrTooComplex(TooComplexNode(node)!);
        }
    }

    private static StringOrTooComplex WalkString(
        Node node, List<BashSimpleCommandInfo> innerCommands, Dictionary<string, string> varScope)
    {
        var result = new StringBuilder();
        var hasPlaceholder = false;

        foreach (var child in node.Children)
        {
            if (child is null) continue;

            switch (child.Type)
            {
                case "\"" or "\"\"":
                    continue;

                case "string_content":
                    result.Append(child.Text);
                    break;

                case "simple_expansion":
                {
                    var v = ResolveSimpleExpansion(child, varScope, insideString: true);
                    if (v.IsTooComplex) return v;
                    var val = v.Value;
                    if (val == VarPlaceholder)
                        hasPlaceholder = true;
                    result.Append(val);
                    break;
                }

                case "command_substitution":
                {
                    var innerScope = new Dictionary<string, string>(varScope);
                    var err = CollectCommandSubstitution(child, innerCommands, innerScope);
                    if (err is not null) return new StringOrTooComplex(err);
                    result.Append(CmdsubPlaceholder);
                    hasPlaceholder = true;
                    break;
                }

                case "concatenation":
                {
                    var part = WalkArgument(child, innerCommands, varScope);
                    if (part.IsTooComplex) return part;
                    result.Append(part.Value);
                    if (ContainsAnyPlaceholder(part.Value))
                        hasPlaceholder = true;
                    break;
                }

                default:
                    return new StringOrTooComplex(TooComplexNode(child)!);
            }
        }

        var str = result.ToString();
        if (hasPlaceholder && !ContainsAnyPlaceholder(str))
            str = VarPlaceholder + str;
        return new StringOrTooComplex(str);
    }

    private static BashAstSecurityResult? WalkArithmetic(Node node)
    {
        return ValidateArithmeticNode(node);
    }

    private static BashAstSecurityResult? ValidateArithmeticNode(Node node)
    {
        if (node is null) return null;

        switch (node.Type)
        {
            case "arithmetic_expansion":
                foreach (var child in node.Children)
                {
                    var err = ValidateArithmeticNode(child);
                    if (err is not null) return err;
                }
                return null;

            case "(":
            case ")":
            case "++":
            case "--":
            case "+":
            case "-":
            case "*":
            case "/":
            case "%":
            case "**":
            case "==":
            case "!=":
            case "<":
            case ">":
            case "<=":
            case ">=":
            case "&&":
            case "||":
            case "!":
            case "~":
            case "^":
            case "|":
            case "&":
            case "<<":
            case ">>":
            case "?":
            case ":":
            case ",":
            case "number":
                break;

            case "variable_name":
            case "word":
                if (node.Type == "word" && node.Children.Count > 0)
                {
                    foreach (var child in node.Children)
                    {
                        var err = ValidateArithmeticNode(child);
                        if (err is not null) return err;
                    }
                    break;
                }
                return new BashAstSecurityResult.TooComplex(
                    $"算术展开中包含变量引用: {node.Text}", "ARITH_VAR");

            case "simple_expansion":
            case "subscript":
                return new BashAstSecurityResult.TooComplex(
                    $"算术展开中包含变量展开: {node.Text}", "ARITH_EXPANSION");

            case "command_substitution":
                return new BashAstSecurityResult.TooComplex(
                    "算术展开中包含命令替换", "ARITH_CMDSUB");

            default:
                foreach (var child in node.Children)
                {
                    var err = ValidateArithmeticNode(child);
                    if (err is not null) return err;
                }
                break;
        }

        return null;
    }
}
