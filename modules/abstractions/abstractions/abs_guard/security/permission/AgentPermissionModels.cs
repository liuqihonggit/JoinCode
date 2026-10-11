namespace JoinCode.Abstractions.Security.Permission;

public enum PermissionLevel {
    [EnumValue("none")] None,
    [EnumValue("read")] Read,
    [EnumValue("write")] Write,
    [EnumValue("execute")] Execute,
    [EnumValue("admin")] Admin
}

public sealed record AgentPermissionRule {
    /// <summary>获取代理名称匹配模式。</summary>
    public required string AgentPattern { get; init; }
    /// <summary>获取权限模式。</summary>
    public required PermissionMode Mode { get; init; }
    /// <summary>获取或设置权限级别。</summary>
    public PermissionLevel Level { get; init; } = PermissionLevel.Read;
    /// <summary>获取允许的工具列表。</summary>
    public List<string>? AllowedTools { get; init; }
    /// <summary>获取拒绝的工具列表。</summary>
    public List<string>? DeniedTools { get; init; }
    /// <summary>获取允许的路径列表。</summary>
    public List<string>? AllowedPaths { get; init; }
    /// <summary>获取拒绝的路径列表。</summary>
    public List<string>? DeniedPaths { get; init; }
    /// <summary>获取规则描述。</summary>
    public string? Description { get; init; }
    /// <summary>获取或设置规则优先级。</summary>
    public int Priority { get; init; } = 0;
}

public sealed record PermissionCheckResult {
    /// <summary>获取一个值，指示是否允许操作。</summary>
    public bool IsAllowed { get; init; }
    /// <summary>获取权限模式。</summary>
    public PermissionMode Mode { get; init; }
    /// <summary>获取决策原因。</summary>
    public string? Reason { get; init; }
    /// <summary>获取匹配的权限规则。</summary>
    public AgentPermissionRule? MatchedRule { get; init; }
    /// <summary>获取一个值，指示是否需要确认。</summary>
    public bool RequiresConfirmation => Mode == PermissionMode.Ask;
    /// <summary>获取一个值，指示是否需要计划。</summary>
    public bool RequiresPlan => Mode == PermissionMode.Plan;
}