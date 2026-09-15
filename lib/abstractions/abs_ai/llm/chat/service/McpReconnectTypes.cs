namespace JoinCode.Abstractions.LLM.Chat;

public enum McpReconnectAcceptLevel
{
    [EnumValue("identity_only")]
    IdentityOnly,
    [EnumValue("identity_and_append")]
    IdentityAndAppend,
    [EnumValue("identity_append_and_reorder")]
    IdentityAppendAndReorder
}

public sealed class McpReconnectResult
{
    public bool Accepted { get; init; }
    public ToolDriftKind DriftKind { get; init; }
    public string Reason { get; init; } = string.Empty;
}
