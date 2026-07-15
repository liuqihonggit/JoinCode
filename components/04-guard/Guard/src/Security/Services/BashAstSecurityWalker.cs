using JoinCode.Abstractions.Attributes;
using TreeSitter;

namespace JoinCode.Abstractions.Security.Shell;

[Register]
public sealed partial class BashAstSecurityWalker : IBashAstSecurityWalker, IDisposable
{
    private const string CmdsubPlaceholder = "__CMDSUB_OUTPUT__";
    private const string VarPlaceholder = "__TRACKED_VAR__";

    private static readonly Regex ControlCharRegex = new(
        @"[\x00-\x08\x0B-\x1F\x7F]", RegexOptions.Compiled);

    private static readonly Regex UnicodeWhitespaceRegex = new(
        @"[\u00A0\u1680\u2000-\u200B\u2028\u2029\u202F\u205F\u3000\uFEFF]",
        RegexOptions.Compiled);

    private static readonly Regex BackslashWhitespaceRegex = new(
        @"\\[ \t]|[^ \t\n\\]\\\n", RegexOptions.Compiled);

    private static readonly Regex ZshTildeBracketRegex = new(@"~\[", RegexOptions.Compiled);

    private static readonly Regex ZshEqualsExpansionRegex = new(
        @"(?:^|[\s;&|])=[a-zA-Z_]", RegexOptions.Compiled);

    private static readonly Regex BraceWithQuoteRegex = new(
        @"\{[^}]*['""]", RegexOptions.Compiled);

    private static readonly Regex BraceExpansionRegex = new(
        @"\{[^{}\s]*(,|\.\.)[^{}\s]*\}", RegexOptions.Compiled);

    private static readonly FrozenSet<string> SafeEnvVars = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "HOME", "PWD", "OLDPWD", "USER", "LOGNAME", "SHELL", "PATH",
        "HOSTNAME", "UID", "EUID", "PPID", "RANDOM", "SECONDS", "LINENO",
        "TMPDIR", "BASH_VERSION", "BASHPID", "SHLVL", "HISTFILE", "IFS");

    private static readonly FrozenSet<string> SpecialVarNames = FrozenSet.Create(
        StringComparer.Ordinal,
        "?", "!", "#", "$", "0", "-", "@", "*");

    private static readonly FrozenSet<string> StructuralTypes = FrozenSet.Create(
        StringComparer.Ordinal,
        "program", "list", "pipeline", "redirected_statement");

    private static readonly FrozenSet<string> SeparatorTypes = FrozenSet.Create(
        StringComparer.Ordinal,
        "&&", "||", "|", "|&", "&", ";;", ";", ";;&", ";&", "\n");

    private static readonly FrozenSet<string> DeclarationCommands = FrozenSet.Create(
        StringComparer.OrdinalIgnoreCase,
        "export", "local", "readonly", "declare", "typeset", "nameref");

    private readonly TreeSitter.Language _language;
    private readonly TreeSitter.Parser _parser;
    private int _disposed;

    public BashAstSecurityWalker()
    {
        _language = new TreeSitter.Language("bash");
        _parser = new TreeSitter.Parser(_language);
    }

    public BashAstSecurityResult ParseForSecurity(string command)
    {
        if (string.IsNullOrEmpty(command))
            return new BashAstSecurityResult.Simple([]);

        var preCheck = RunPreChecks(command);
        if (preCheck is not null) return preCheck;

        var trimmed = command.Trim();
        if (trimmed.Length == 0)
            return new BashAstSecurityResult.Simple([]);

        Node? root;
        try
        {
            var tree = _parser.Parse(command);
            root = tree?.RootNode;
        }
        catch (Exception ex)
        {
            return new BashAstSecurityResult.TooComplex(
                $"Bash解析异常: {ex.Message}", "PARSE_EXCEPTION");
        }

        if (root is null)
            return new BashAstSecurityResult.TooComplex("TreeSitter解析失败", "PARSE_ERROR");

        if (HasErrorNode(root))
            return new BashAstSecurityResult.TooComplex("AST包含错误节点", "PARSE_ERROR");

        return WalkProgram(root);
    }

    private static BashAstSecurityResult WalkProgram(Node root)
    {
        var commands = new List<BashSimpleCommandInfo>();
        var varScope = new Dictionary<string, string>();

        var err = CollectCommands(root, commands, varScope);
        if (err is not null) return err;

        return new BashAstSecurityResult.Simple([.. commands]);
    }

    public void Dispose()
    {
        if (!DisposableHelper.TryMarkDisposed(ref _disposed)) return;
        _parser.Dispose();
        _language.Dispose();
    }
}
