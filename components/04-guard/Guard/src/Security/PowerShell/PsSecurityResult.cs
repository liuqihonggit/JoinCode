namespace JoinCode.Guard.Security.PowerShell;

public sealed record PsSecurityResult
{
    public required PermissionBehavior Behavior { get; init; }

    public string? Message { get; init; }

    public string? BlockedPath { get; init; }

    public string? Suggestions { get; init; }

    public string? DecisionReason { get; init; }

    public static readonly PsSecurityResult Passthrough = new() { Behavior = PermissionBehavior.Passthrough };
    public static PsSecurityResult Ask(string message) => new() { Behavior = PermissionBehavior.Ask, Message = message };
    public static PsSecurityResult Deny(string message, string? blockedPath = null) => new() { Behavior = PermissionBehavior.Deny, Message = message, BlockedPath = blockedPath };

    public JoinCode.Abstractions.Security.Shell.PowerShell.PsSecurityResult ToAbstractionsResult() => new()
    {
        Behavior = Behavior,
        Message = Message,
        BlockedPath = BlockedPath,
        Suggestions = Suggestions,
        DecisionReason = DecisionReason,
    };
}
