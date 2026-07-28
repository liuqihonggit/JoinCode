namespace JoinCode.Abstractions.Security.Shell.PowerShell;

public sealed record PsSecurityResult : ShellPermissionCheckResult
{
    public string? BlockedPath { get; init; }

    public string? Suggestions { get; init; }

    public string? DecisionReason { get; init; }

    public PsSecurityResult() : base(PermissionBehavior.Passthrough) { }

    public PsSecurityResult(PermissionBehavior behavior, string? message = null) : base(behavior, message) { }

    public static readonly PsSecurityResult Passthrough = new() { Behavior = PermissionBehavior.Passthrough };
    public static PsSecurityResult Ask(string message) => new(PermissionBehavior.Ask, message);
    public static PsSecurityResult Deny(string message, string? blockedPath = null) => new(PermissionBehavior.Deny, message) { BlockedPath = blockedPath };
}
