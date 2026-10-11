namespace JoinCode.Abstractions.LLM.Chat;

public sealed record TranscriptEntry {
    /// <summary>获取会话标识。</summary>
    public string SessionId { get; init; } = string.Empty;
    /// <summary>获取角色。</summary>
    public string Role { get; init; } = string.Empty;
    /// <summary>获取内容。</summary>
    public string Content { get; init; } = string.Empty;
    /// <summary>获取时间戳。</summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    /// <summary>获取模型标识。</summary>
    public string? ModelId { get; init; }
    /// <summary>获取提示词 Token 数。</summary>
    public int PromptTokens { get; init; }
    /// <summary>获取补全 Token 数。</summary>
    public int CompletionTokens { get; init; }
    /// <summary>获取代理标识。</summary>
    public string? AgentId { get; init; }
    /// <summary>获取是否为侧链。</summary>
    public bool IsSidechain { get; init; }
    /// <summary>获取工具名称。</summary>
    public string? ToolName { get; init; }
    /// <summary>获取工具使用标识。</summary>
    public string? ToolUseId { get; init; }

    /// <summary>
    /// 条目类型 — 对齐 TS append-only 元数据模式。
    /// null = 普通消息, "custom-title" = 用户重命名, "agent-name" = 代理名称
    /// </summary>
    public string? Type { get; init; }

    /// <summary>
    /// 自定义标题 — 对齐 TS custom-title 条目
    /// </summary>
    public string? CustomTitle { get; init; }

    /// <summary>
    /// 代理名称 — 对齐 TS agent-name 条目
    /// </summary>
    public string? AgentName { get; init; }

    /// <summary>
    /// 粘贴内容哈希引用 — 对齐 TS StoredPastedContent.contentHash
    /// 大文本(>1024字符)不在 Content 中内联存储，而是存到 paste-cache/ 目录，
    /// 此字段存储 SHA-256 前 16 位哈希作为引用。反序列化时通过 IPasteStore.RetrievePastedText 还原
    /// </summary>
    public string? ContentHash { get; init; }

    /// <summary>
    /// 创建带有指定 SessionId 的副本
    /// </summary>
    public TranscriptEntry WithSessionId(string sessionId) => this with { SessionId = sessionId };

    /// <summary>
    /// 创建带有 Agent 元数据的副本（设置 SessionId、AgentId、IsSidechain）
    /// </summary>
    public TranscriptEntry WithAgentMeta(string sessionId, string agentId) =>
        this with { SessionId = sessionId, AgentId = agentId, IsSidechain = true };
}

public sealed class TranscriptSummary {
    /// <summary>获取会话标识。</summary>
    public string SessionId { get; init; } = string.Empty;
    /// <summary>获取创建时间。</summary>
    public DateTime CreatedAt { get; init; }
    /// <summary>获取最后修改时间。</summary>
    public DateTime LastModifiedAt { get; init; }
    /// <summary>获取消息数量。</summary>
    public int MessageCount { get; init; }
    /// <summary>获取最后消息预览。</summary>
    public string? LastMessagePreview { get; init; }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull, AllowTrailingCommas = true, ReadCommentHandling = JsonCommentHandling.Skip, PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(TranscriptEntry))]
[JsonSerializable(typeof(TranscriptSummary))]
[JsonSerializable(typeof(List<TranscriptEntry>))]
[JsonSerializable(typeof(List<TranscriptSummary>))]
[JsonSerializable(typeof(SessionInfo))]
public sealed partial class TranscriptJsonContext : JsonSerializerContext;