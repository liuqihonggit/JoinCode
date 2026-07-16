using JoinCode.Abstractions.Attributes;
using TreeSitter;

namespace JoinCode.Abstractions.Security.Shell;

[Register]
public sealed partial class BashAstSecurityWalker : IBashAstSecurityWalker, IDisposable
{
    private const string CmdsubPlaceholder = "__CMDSUB_OUTPUT__";
    private const string VarPlaceholder = "__TRACKED_VAR__";

    private static readonly Regex BraceExpansionRegex = new(
        @"\{[^{}\s]*(,|\.\.)[^{}\s]*\}", RegexOptions.Compiled);

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
