namespace JoinCode.CodeIndex.Ast;

/// <summary>
/// Bash AST 解析器 — 使用 TreeSitter.DotNet 解析 bash 命令
/// 对齐 TS ast.ts 的 parseForSecurity 功能
/// 语义检查和常量委托给 BashSemanticChecker / BashSecurityConstants / BashSafeWrapperStripper
/// </summary>
public sealed partial class BashAstParser : IDisposable {
    private readonly Language _language;
    private readonly Parser _parser;
    private int _disposed;

    /// <summary>
    /// 构造 BashAstParser — 初始化 TreeSitter bash 语言和解析器
    /// </summary>
    public BashAstParser() {
        _language = new Language("bash");
        _parser = new Parser(_language);
    }

    /// <summary>
    /// 解析 bash 命令为 AST 根节点
    /// </summary>
    /// <param name="command">bash 命令字符串（超过 10000 字符返回 null）</param>
    /// <returns>AST 根节点；解析失败或空命令返回 null</returns>
    public Node? Parse(string command) {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        if (string.IsNullOrEmpty(command) || command.Length > 10_000)
            return null;

        try {
            var tree = _parser.Parse(command);
            return tree?.RootNode;
        } catch {
            return null;
        }
    }

    /// <summary>
    /// 从 AST 根节点提取所有简单命令信息
    /// </summary>
    /// <param name="root">AST 根节点</param>
    /// <returns>简单命令信息列表</returns>
    public static List<BashSimpleCommandInfo> ExtractSimpleCommands(Node root) {
        var commands = new List<BashSimpleCommandInfo>();
        WalkForCommands(root, commands);
        return commands;
    }

    /// <summary>
    /// 安全解析 — 解析 bash 命令并提取简单命令用于安全检查
    /// </summary>
    /// <param name="command">bash 命令字符串</param>
    /// <returns>安全解析结果（Simple/ParseUnavailable/TooComplex）</returns>
    public BashAstSecurityResult ParseForSecurity(string command) {
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

    /// <summary>
    /// 语义检查 — 委托给 BashSemanticChecker 检查命令语义
    /// </summary>
    /// <param name="commands">简单命令数组</param>
    /// <returns>语义检查结果</returns>
    public static BashSemanticCheckResult CheckSemantics(BashSimpleCommandInfo[] commands)
        => BashSemanticChecker.CheckSemantics(commands);

    private static bool HasErrorNode(Node node) {
        if (node.IsError || node.IsMissing) return true;
        foreach (var child in node.Children) {
            if (HasErrorNode(child)) return true;
        }
        return false;
    }

    private static void WalkForCommands(Node node, List<BashSimpleCommandInfo> commands) {
        switch (node.Type) {
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

    private static void ExtractRedirectedStatement(Node node, List<BashSimpleCommandInfo> commands) {
        BashSimpleCommandInfo? baseCmd = null;
        var redirects = new List<BashRedirectInfo>();

        foreach (var child in node.Children) {
            switch (child.Type) {
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

        if (baseCmd is not null && redirects.Count > 0) {
            var mergedRedirects = baseCmd.Redirects.ToList();
            mergedRedirects.AddRange(redirects);
            baseCmd = baseCmd with { Redirects = [.. mergedRedirects] };
        }

        if (baseCmd is not null) commands.Add(baseCmd);
    }

    private static BashSimpleCommandInfo? ExtractCommand(Node commandNode) {
        var argv = new List<string>();
        var envVars = new List<BashEnvVarInfo>();
        var redirects = new List<BashRedirectInfo>();

        foreach (var child in commandNode.Children) {
            switch (child.Type) {
                case "variable_assignment":
                var eqIdx = child.Text.IndexOf('=');
                if (eqIdx > 0) {
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

    private static BashRedirectInfo? ExtractRedirect(Node redirectNode) {
        var children = redirectNode.Children;
        if (children.Count == 0) return null;

        var op = "";
        var target = "";
        var fd = -1;

        foreach (var child in children) {
            switch (child.Type) {
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

    private static string StripQuotes(string text) {
        if (text.Length >= 2) {
            if ((text[0] == '"' && text[^1] == '"') ||
                (text[0] == '\'' && text[^1] == '\''))
                return text[1..^1];
        }
        return text;
    }

    /// <summary>
    /// 释放 TreeSitter 解析器和语言资源
    /// </summary>
    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _parser.Dispose();
        _language.Dispose();
    }
}