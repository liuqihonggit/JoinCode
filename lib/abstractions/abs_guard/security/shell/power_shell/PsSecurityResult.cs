namespace JoinCode.Abstractions.Security.Shell.PowerShell;

public sealed record PsSecurityResult : ShellPermissionCheckResult {
    /// <summary>获取被阻止的路径。</summary>
    public string? BlockedPath { get; init; }

    /// <summary>获取建议文本。</summary>
    public string? Suggestions { get; init; }

    /// <summary>获取决策原因。</summary>
    public string? DecisionReason { get; init; }

    /// <summary>构造默认实例。</summary>
    public PsSecurityResult() : base(PermissionBehavior.Passthrough) { }

    /// <summary>构造 PowerShell 安全结果。</summary>
    public PsSecurityResult(PermissionBehavior behavior, string? message = null) : base(behavior, message) { }

    public static readonly PsSecurityResult Passthrough = new() { Behavior = PermissionBehavior.Passthrough };
    /// <summary>创建询问结果。</summary>
    public static PsSecurityResult Ask(string message) => new(PermissionBehavior.Ask, message);
    /// <summary>创建拒绝结果。</summary>
    public static PsSecurityResult Deny(string message, string? blockedPath = null) => new(PermissionBehavior.Deny, message) { BlockedPath = blockedPath };
}