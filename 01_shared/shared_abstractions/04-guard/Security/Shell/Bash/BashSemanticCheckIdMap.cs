namespace JoinCode.Abstractions.Security.Shell;

public sealed record BashSemanticCheckIdMap(
    BashSecurityCheckId EmptyCommandName,
    BashSecurityCheckId IncompleteFragment,
    BashSecurityCheckId ShellKeywords,
    BashSecurityCheckId ZshDangerousBuiltins,
    BashSecurityCheckId EvalLikeBuiltins)
{
    public static readonly BashSemanticCheckIdMap Default = new(
        BashSecurityCheckId.EmptyCommandName,
        BashSecurityCheckId.IncompleteFragment,
        BashSecurityCheckId.ShellKeywords,
        BashSecurityCheckId.ZshDangerousBuiltins,
        BashSecurityCheckId.EvalLikeBuiltins);

    public static readonly BashSemanticCheckIdMap Walker = new(
        BashSecurityCheckId.IncompleteCommands,
        BashSecurityCheckId.IncompleteCommands,
        BashSecurityCheckId.ObfuscatedFlags,
        BashSecurityCheckId.ZshDangerousCommands,
        BashSecurityCheckId.DangerousVariables);
}
