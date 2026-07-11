namespace JoinCode.Abstractions.LLM.Chat;

public enum McpReconnectAcceptLevel
{
    IdentityOnly,
    IdentityAndAppend,
    IdentityAppendAndReorder
}

public sealed class McpReconnectResult
{
    public bool Accepted { get; init; }
    public ToolDriftKind DriftKind { get; init; }
    public string Reason { get; init; } = string.Empty;
}
