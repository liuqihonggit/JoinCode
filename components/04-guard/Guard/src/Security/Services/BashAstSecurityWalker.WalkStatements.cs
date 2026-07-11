using TreeSitter;

namespace JoinCode.Abstractions.Security.Shell;

public sealed partial class BashAstSecurityWalker
{
    private static BashAstSecurityResult? WalkForStatement(
        Node node, List<BashSimpleCommandInfo> commands, Dictionary<string, string> varScope)
    {
        foreach (var child in node.Children)
        {
            if (child is null) continue;

            if (child.Type == "variable_name")
            {
                varScope[child.Text] = VarPlaceholder;
            }
            else if (child.Type == "do_group" || child.Type == "compound_list")
            {
                var bodyScope = new Dictionary<string, string>(varScope);
                var err = CollectCommands(child, commands, bodyScope);
                if (err is not null) return err;
            }
            else
            {
                var err = CollectCommands(child, commands, varScope);
                if (err is not null) return err;
            }
        }

        return null;
    }

    private static BashAstSecurityResult? WalkConditionalStatement(
        Node node, List<BashSimpleCommandInfo> commands, Dictionary<string, string> varScope)
    {
        foreach (var child in node.Children)
        {
            if (child is null) continue;

            if (child.Type == "command" || child.Type == "pipeline" || child.Type == "list")
            {
                var err = CollectCommands(child, commands, varScope);
                if (err is not null) return err;
            }
            else if (child.Type == "do_group" || child.Type == "compound_list"
                     || child.Type == "then_clause" || child.Type == "else_clause"
                     || child.Type == "elif_clause")
            {
                var branchScope = new Dictionary<string, string>(varScope);
                var err = CollectCommands(child, commands, branchScope);
                if (err is not null) return err;
            }
            else
            {
                var branchScope = new Dictionary<string, string>(varScope);
                var err = CollectCommands(child, commands, branchScope);
                if (err is not null) return err;
            }
        }

        return null;
    }

    private static BashAstSecurityResult? WalkSubshell(
        Node node, List<BashSimpleCommandInfo> commands, Dictionary<string, string> varScope)
    {
        var innerScope = new Dictionary<string, string>(varScope);
        foreach (var child in node.Children)
        {
            if (child is null) continue;
            var err = CollectCommands(child, commands, innerScope);
            if (err is not null) return err;
        }
        return null;
    }

    private static BashAstSecurityResult? WalkTestCommand(Node node)
    {
        return TooComplexNode(node);
    }

    private static BashAstSecurityResult? WalkUnsetCommand(
        Node node, Dictionary<string, string> varScope)
    {
        foreach (var child in node.Children)
        {
            if (child is not null && child.Type == "word")
            {
                varScope.Remove(child.Text);
            }
        }
        return null;
    }
}
