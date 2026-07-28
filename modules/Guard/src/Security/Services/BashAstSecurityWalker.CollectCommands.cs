using TreeSitter;

namespace JoinCode.Abstractions.Security.Shell;

public sealed partial class BashAstSecurityWalker
{
    private static BashAstSecurityResult? CollectCommands(
        Node node, List<BashSimpleCommandInfo> commands, Dictionary<string, string> varScope)
    {
        switch (node.Type)
        {
            case "command":
                return WalkCommand(node, [], commands, varScope);

            case "redirected_statement":
                return WalkRedirectedStatement(node, commands, varScope);

            case "comment":
                return null;

            case "negated_command":
            {
                foreach (var child in node.Children)
                {
                    if (child is null) continue;
                    var err = CollectCommands(child, commands, varScope);
                    if (err is not null) return err;
                }
                return null;
            }

            case "declaration_command":
                return WalkDeclarationCommand(node, commands, varScope);

            case "variable_assignment":
                return WalkStandaloneVariableAssignment(node, varScope);

            case "for_statement":
                return WalkForStatement(node, commands, varScope);

            case "if_statement":
            case "while_statement":
            case "until_statement":
                return WalkConditionalStatement(node, commands, varScope);

            case "subshell":
                return WalkSubshell(node, commands, varScope);

            case "test_command":
                return WalkTestCommand(node);

            case "unset_command":
                return WalkUnsetCommand(node, varScope);

            case "case_statement":
            case "function_definition":
            case "c_style_for_statement":
            case "command_substitution":
                return TooComplexNode(node);
        }

        if (BashSecurityConstants.StructuralTypes.Contains(node.Type))
        {
            return WalkStructuralNode(node, commands, varScope);
        }

        return TooComplexNode(node);
    }

    private static BashAstSecurityResult? WalkStructuralNode(
        Node node, List<BashSimpleCommandInfo> commands, Dictionary<string, string> varScope)
    {
        var isPipeline = node.Type == "pipeline";

        var needsSnapshot = false;
        if (!isPipeline)
        {
            foreach (var child in node.Children)
            {
                if (child is not null && (child.Type == "||" || child.Type == "&"))
                {
                    needsSnapshot = true;
                    break;
                }
            }
        }

        var snapshot = needsSnapshot ? new Dictionary<string, string>(varScope) : null;
        var scope = isPipeline ? new Dictionary<string, string>(varScope) : varScope;

        foreach (var child in node.Children)
        {
            if (child is null) continue;

            if (BashSecurityConstants.SeparatorTypes.Contains(child.Type))
            {
                if (child.Type is "||" or "|" or "|&" or "&")
                {
                    scope = new Dictionary<string, string>(snapshot ?? varScope);
                }
                continue;
            }

            var err = CollectCommands(child, commands, scope);
            if (err is not null) return err;
        }

        return null;
    }
}
