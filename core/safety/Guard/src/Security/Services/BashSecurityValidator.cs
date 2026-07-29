using JoinCode.Abstractions.Attributes;

namespace JoinCode.Abstractions.Security.Shell;

[Register]
public sealed partial class BashSecurityValidator : IBashSecurityValidator
{
    [Inject] private readonly IBashAstSecurityWalker _astWalker;

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
