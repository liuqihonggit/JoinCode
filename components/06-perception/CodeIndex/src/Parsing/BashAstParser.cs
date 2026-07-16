namespace JoinCode.CodeIndex.Ast;

/// <summary>
/// Bash AST 解析器 — 使用 TreeSitter.DotNet 解析 bash 命令
/// 对齐 TS ast.ts 的 parseForSecurity 功能
/// 语义检查和常量委托给 BashSemanticChecker / BashSecurityConstants / BashSafeWrapperStripper
/// </summary>
public sealed partial class BashAstParser : IDisposable
{
    private readonly Language _language;
    private readonly Parser _parser;
    private int _disposed;

    public BashAstParser()
    {
        _language = new Language("bash");
        _parser = new Parser(_language);
    }

    public Node? Parse(string command)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        if (string.IsNullOrEmpty(command) || command.Length > 10_000)
            return null;

        try
        {
            var tree = _parser.Parse(command);
            return tree?.RootNode;
        }
        catch
        {
            return null;
        }
    }

    public static List<BashSimpleCommandInfo> ExtractSimpleCommands(Node root)
    {
        var commands = new List<BashSimpleCommandInfo>();
        WalkForCommands(root, commands);
        return commands;
    }

    public BashAstSecurityResult ParseForSecurity(string command)
    {
        var root = Parse(command);
        if (root is null)
            return new BashAstSecurityResult.ParseUnavailable("tree-sitter 解析失败");

        if (HasErrorNode(root))
            return new BashAstSecurityResult.TooComplex("AST 包含错误节点");

        var commands = ExtractSimpleCommands(root);
        if (commands.Count == 0)
            return new BashAstSecurityResult.TooComplex("无法提取任何命令");

        return new BashAstSecurityResult.Simple([.. commands]);
    }

    public static BashSemanticCheckResult CheckSemantics(BashSimpleCommandInfo[] commands)
        => BashSemanticChecker.CheckSemantics(commands);

    private static bool HasErrorNode(Node node)
    {
        if (node.IsError || node.IsMissing) return true;
        foreach (var child in node.Children)
        {
            if (HasErrorNode(child)) return true;
        }
        return false;
    }

    private static void WalkForCommands(Node node, List<BashSimpleCommandInfo> commands)
    {
        switch (node.Type)
        {
            case "program":
            case "list":
            case "pipeline":
            case "subshell":
            case "compound_statement":
            case "if_command":
            case "while_command":
            case "for_command":
            case "case_command":
            case "function_definition":
                foreach (var child in node.Children)
                    WalkForCommands(child, commands);
                break;

            case "redirected_statement":
                ExtractRedirectedStatement(node, commands);
                break;

            case "command":
            case "declaration_command":
                var cmd = ExtractCommand(node);
                if (cmd is not null) commands.Add(cmd);
                break;
        }
    }

    private static void ExtractRedirectedStatement(Node node, List<BashSimpleCommandInfo> commands)
    {
        BashSimpleCommandInfo? baseCmd = null;
        var redirects = new List<BashRedirectInfo>();

        foreach (var child in node.Children)
        {
            switch (child.Type)
            {
                case "command":
                case "declaration_command":
                    baseCmd = ExtractCommand(child);
                    break;

                case "file_redirect":
                    var redirect = ExtractRedirect(child);
                    if (redirect is not null) redirects.Add(redirect);
                    break;

                default:
                    WalkForCommands(child, commands);
                    break;
            }
        }

        if (baseCmd is not null && redirects.Count > 0)
        {
            var mergedRedirects = baseCmd.Redirects.ToList();
            mergedRedirects.AddRange(redirects);
            baseCmd = baseCmd with { Redirects = [.. mergedRedirects] };
        }

        if (baseCmd is not null) commands.Add(baseCmd);
    }

    private static BashSimpleCommandInfo? ExtractCommand(Node commandNode)
    {
        var argv = new List<string>();
        var envVars = new List<BashEnvVarInfo>();
        var redirects = new List<BashRedirectInfo>();

        foreach (var child in commandNode.Children)
        {
            switch (child.Type)
            {
                case "variable_assignment":
                    var eqIdx = child.Text.IndexOf('=');
                    if (eqIdx > 0)
                    {
                        envVars.Add(new BashEnvVarInfo(
                            child.Text[..eqIdx],
                            eqIdx + 1 < child.Text.Length ? child.Text[(eqIdx + 1)..] : ""));
                    }
                    break;

                case "command_name":
                    argv.Add(StripQuotes(child.Text));
                    break;

                case "word":
                case "string":
                case "raw_string":
                case "number":
                    argv.Add(StripQuotes(child.Text));
                    break;

                case "simple_expansion":
                case "expansion":
                case "command_substitution":
                case "arithmetic_expansion":
                    argv.Add(child.Text);
                    break;

                case "file_redirect":
                    var redirect = ExtractRedirect(child);
                    if (redirect is not null) redirects.Add(redirect);
                    break;
            }
        }

        if (argv.Count == 0) return null;

        return new BashSimpleCommandInfo(
            [.. argv],
            [.. envVars],
            [.. redirects],
            commandNode.Text);
    }

    private static BashRedirectInfo? ExtractRedirect(Node redirectNode)
    {
        var children = redirectNode.Children;
        if (children.Count == 0) return null;

        var op = "";
        var target = "";
        var fd = -1;

        foreach (var child in children)
        {
            switch (child.Type)
            {
                case "file_descriptor":
                    fd = int.TryParse(child.Text, out var f) ? f : -1;
                    break;
                case ">":
                case ">>":
                case "<":
                case ">&":
                case "<&":
                case ">|":
                case "&>":
                case "&>>":
                    op = child.Type;
                    break;
                case "word":
                case "string":
                case "simple_expansion":
                    target = child.Text;
                    break;
            }
        }

        if (string.IsNullOrEmpty(op)) return null;

        return new BashRedirectInfo(op, target, fd >= 0 ? fd : null);
    }

    private static string StripQuotes(string text)
    {
        if (text.Length >= 2)
        {
            if ((text[0] == '"' && text[^1] == '"') ||
                (text[0] == '\'' && text[^1] == '\''))
                return text[1..^1];
        }
        return text;
    }

    public void Dispose()
    {
        if (!DisposableHelper.TryMarkDisposed(ref _disposed)) return;
        _parser.Dispose();
        _language.Dispose();
    }
}
