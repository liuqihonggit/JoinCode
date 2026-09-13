namespace JoinCode.Abstractions.Security.Shell;

/// <summary>
/// Bash 安全验证器实现 — 基于 AST 解析与正则检查双重路径检测命令注入、混淆、绕过等安全威胁
/// </summary>
[Register(typeof(IBashSecurityValidator), ServiceLifetime.Singleton)]
public sealed partial class BashSecurityValidator : ServiceEntity, IBashSecurityValidator
{

    /// <summary>
    /// 构造 Bash 安全验证器
    /// </summary>
    /// <param name="astWalker">Bash AST 安全遍历器,用于解析命令并执行语义检查</param>
    public BashSecurityValidator(IBashAstSecurityWalker astWalker)
    {
        _astWalker = astWalker;
    }
    private readonly IBashAstSecurityWalker _astWalker;

    /// <inheritdoc/>
    public BashSecurityResult Validate(string command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return new BashSecurityResult(true);
        }

        var astResult = _astWalker.ParseForSecurity(command);
        if (astResult is BashAstSecurityResult.Simple simple)
        {
            var semanticResult = _astWalker.CheckSemantics(simple.Commands);
            if (!semanticResult.IsOk)
            {
                return new BashSecurityResult(false,
                    semanticResult.CheckId,
                    semanticResult.Reason,
                    false);
            }

            return new BashSecurityResult(true);
        }

        foreach (var item in BashRegexCheckRegistry.All)
        {
            if (item.CheckId == BashSecurityCheckId.CommandSubstitution &&
                BashRegexCheckRegistry.IsSafeHeredoc(command))
            {
                continue;
            }

            var result = item.Validate(command);
            if (!result.IsSafe) return result;
        }

        return new BashSecurityResult(true);
    }
}
