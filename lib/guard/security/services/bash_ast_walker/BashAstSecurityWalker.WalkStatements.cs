namespace JoinCode.Abstractions.Security.Shell;

public sealed partial class BashAstSecurityWalker {
    private static BashAstSecurityResult? WalkForStatement(
        Node node, List<BashSimpleCommandInfo> commands, Dictionary<string, string> varScope) {
        // 对齐 TS collectCommands for_statement 分支:
        // 先收集 loopVar 与 doGroup,跳过结构 token,验证迭代值,最后处理 body。
        string? loopVar = null;
        Node? doGroup = null;
        foreach (var child in node.Children) {
            if (child is null) continue;

            if (child.Type == "variable_name") {
                loopVar = child.Text;
            } else if (child.Type == "do_group") {
                doGroup = child;
            } else if (child.Type is "for" or "in" or "select" or ";") {
                continue; // 结构 token — 跳过
            } else if (child.Type == "command_substitution") {
                // for i in $(seq 1 3) — 内部命令需提取并规则检查
                var innerScope = new Dictionary<string, string>(varScope);
                var err = CollectCommandSubstitution(child, commands, innerScope);
                if (err is not null) return err;
            } else if (child.Type == "compound_list") {
                var compoundScope = new Dictionary<string, string>(varScope);
                var err = CollectCommands(child, commands, compoundScope);
                if (err is not null) return err;
            } else {
                // 迭代值 — 通过 WalkArgument 验证(拒绝非法展开),值本身丢弃
                var arg = WalkArgument(child, commands, varScope);
                if (arg.IsTooComplex) return arg.GetTooComplex();
            }
        }

        if (loopVar is null || doGroup is null)
            return TooComplexNode(node);

        // PS4/IFS 作为循环变量绕过赋值验证 — 禁止
        if (loopVar is "PS4" or "IFS")
            return new BashAstSecurityResult.TooComplex(
                $"{loopVar} as loop variable bypasses assignment validation", "for_statement");

        // 循环变量始终为 placeholder — 裸 $i 在 body 中 → too-complex,仅字符串内展开安全
        varScope[loopVar] = VarPlaceholder;
        var bodyScope = new Dictionary<string, string>(varScope);
        foreach (var c in doGroup.Children) {
            if (c is null) continue;
            if (c.Type is "do" or "done" or ";") continue;
            var err = CollectCommands(c, commands, bodyScope);
            if (err is not null) return err;
        }

        return null;
    }

    private static BashAstSecurityResult? WalkConditionalStatement(
        Node node, List<BashSimpleCommandInfo> commands, Dictionary<string, string> varScope) {
        foreach (var child in node.Children) {
            if (child is null) continue;

            // 跳过 if/while/until 的结构关键字 token — 对齐 TS collectCommands if_statement/while_statement 分支
            if (child.Type is "if" or "fi" or "else" or "elif" or "while" or "until"
                or "then" or ";") {
                continue;
            }

            if (child.Type == "do_group") {
                // while body: 递归子节点,scope copy(body 赋值不泄漏过 done)
                var bodyScope = new Dictionary<string, string>(varScope);
                foreach (var c in child.Children) {
                    if (c is null) continue;
                    if (c.Type is "do" or "done" or ";") continue;
                    var err = CollectCommands(c, commands, bodyScope);
                    if (err is not null) return err;
                }
            } else if (child.Type is "elif_clause" or "else_clause") {
                // elif/else 分支:递归子节点,scope copy
                var branchScope = new Dictionary<string, string>(varScope);
                foreach (var c in child.Children) {
                    if (c is null) continue;
                    if (c.Type is "elif" or "else" or "then" or ";") continue;
                    var err = CollectCommands(c, commands, branchScope);
                    if (err is not null) return err;
                }
            } else if (child.Type == "compound_list" || child.Type == "then_clause") {
                var branchScope = new Dictionary<string, string>(varScope);
                var err = CollectCommands(child, commands, branchScope);
                if (err is not null) return err;
            } else {
                // 条件命令或 then-body 命令 — 用真实 varScope
                var err = CollectCommands(child, commands, varScope);
                if (err is not null) return err;
            }
        }

        return null;
    }

    private static BashAstSecurityResult? WalkSubshell(
        Node node, List<BashSimpleCommandInfo> commands, Dictionary<string, string> varScope) {
        var innerScope = new Dictionary<string, string>(varScope);
        foreach (var child in node.Children) {
            if (child is null) continue;
            // 跳过 subshell 的括号 token 和命令分隔符 — 对齐 TS collectCommands subshell 分支
            if (child.Type is "(" or ")") continue;
            if (BashSecurityConstants.SeparatorTypes.Contains(child.Type)) continue;
            var err = CollectCommands(child, commands, innerScope);
            if (err is not null) return err;
        }
        return null;
    }

    private static BashAstSecurityResult? WalkTestCommand(Node node) {
        return TooComplexNode(node);
    }

    private static BashAstSecurityResult? WalkUnsetCommand(
        Node node, List<BashSimpleCommandInfo> commands, Dictionary<string, string> varScope) {
        // 对齐 TS collectCommands unset_command 分支:提取 argv 并产生命令,
        // 同时从 varScope 移除变量使后续 $VAR 引用正确拒绝。
        var argv = new List<string>();
        foreach (var child in node.Children) {
            if (child is null) continue;
            switch (child.Type) {
                case "unset":
                argv.Add(child.Text);
                break;
                case "variable_name":
                argv.Add(child.Text);
                varScope.Remove(child.Text);
                break;
                case "word":
                argv.Add(child.Text);
                break;
                default:
                return TooComplexNode(child);
            }
        }

        if (argv.Count > 0)
            commands.Add(new BashSimpleCommandInfo([.. argv], [], [], node.Text));
        return null;
    }
}