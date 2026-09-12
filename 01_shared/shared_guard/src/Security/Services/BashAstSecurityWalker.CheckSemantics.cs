namespace JoinCode.Abstractions.Security.Shell;

public sealed partial class BashAstSecurityWalker
{
    public BashSemanticCheckResult CheckSemantics(BashSimpleCommandInfo[] commands)
    {
        return BashSemanticChecker.CheckSemantics(
            commands,
            BashSemanticCheckIdMap.Walker,
            name => ContainsAnyPlaceholder(name)
                ? new BashSemanticCheckResult(false, $"命令名包含占位符: {name}", BashSecurityCheckId.DangerousVariables)
                : null);
    }
}
