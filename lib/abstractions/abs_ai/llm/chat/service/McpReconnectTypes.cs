namespace JoinCode.Abstractions.LLM.Chat;

/// <summary>MCP 重连接受级别枚举。</summary>
public enum McpReconnectAcceptLevel {
    [EnumValue("identity_only")]
    IdentityOnly,
    [EnumValue("identity_and_append")]
    IdentityAndAppend,
    [EnumValue("identity_append_and_reorder")]
    IdentityAppendAndReorder
}

/// <summary>MCP 重连结果。</summary>
public sealed class McpReconnectResult {
    /// <summary>获取是否被接受。</summary>
    public bool Accepted { get; init; }
    /// <summary>获取工具漂移种类。</summary>
    public ToolDriftKind DriftKind { get; init; }
    /// <summary>获取原因说明。</summary>
    public string Reason { get; init; } = string.Empty;
}
