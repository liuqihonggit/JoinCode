namespace JoinCode.Abstractions.Security.Permission;

public enum PermissionLevel
{
    [EnumValue("none")] None,
    [EnumValue("read")] Read,
    [EnumValue("write")] Write,
    [EnumValue("execute")] Execute,
    [EnumValue("admin")] Admin
}

public sealed record AgentPermissionRule
{
    public required string AgentPattern { get; init; }
    public required PermissionMode Mode { get; init; }
    public PermissionLevel Level { get; init; } = PermissionLevel.Read;
    public List<string>? AllowedTools { get; init; }
    public List<string>? DeniedTools { get; init; }
    public List<string>? AllowedPaths { get; init; }
    public List<string>? DeniedPaths { get; init; }
    public string? Description { get; init; }
    public int Priority { get; init; } = 0;
}

public sealed record PermissionCheckResult
{
    public bool IsAllowed { get; init; }
    public PermissionMode Mode { get; init; }
    public string? Reason { get; init; }
    public AgentPermissionRule? MatchedRule { get; init; }
    public bool RequiresConfirmation => Mode == PermissionMode.Ask;
    public bool RequiresPlan => Mode == PermissionMode.Plan;
}
