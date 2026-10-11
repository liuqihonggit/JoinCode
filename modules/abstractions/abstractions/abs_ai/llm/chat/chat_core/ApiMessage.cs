namespace JoinCode.Abstractions.LLM.Chat;

public sealed class ApiMessage {
    /// <summary>获取消息角色。</summary>
    public MessageRole Role { get; init; }
    /// <summary>获取消息文本内容。</summary>
    public string? Content { get; init; }
    /// <summary>获取元数据字典。</summary>
    public IReadOnlyDictionary<string, JsonElement> Metadata { get; init; } = new Dictionary<string, JsonElement>();
    /// <summary>获取模型标识。</summary>
    public string? ModelId { get; init; }
    /// <summary>获取令牌使用情况。</summary>
    public TokenUsage? TokenUsage { get; init; }

    /// <summary>
    /// 多模态内容块 — 对齐 TS Anthropic ContentBlock (image/document)
    /// 当工具结果包含图片或二进制内容时，此字段承载非文本内容块
    /// ChatService 负责将这些内容块转换为 LLM API 的多模态格式
    /// </summary>
    public IReadOnlyList<ToolContent> ContentBlocks { get; init; } = [];

    /// <summary>构造默认 ApiMessage 实例。</summary>
    public ApiMessage() { }

    /// <summary>构造 ApiMessage 实例。</summary>
    /// <param name="role">消息角色。</param>
    /// <param name="content">消息内容。</param>
    /// <param name="metadata">元数据字典。</param>
    /// <param name="modelId">模型标识。</param>
    /// <param name="tokenUsage">令牌使用情况。</param>
    public ApiMessage(MessageRole role, string? content, IReadOnlyDictionary<string, JsonElement>? metadata = null, string? modelId = null, TokenUsage? tokenUsage = null) {
        Role = role;
        Content = content;
        Metadata = metadata ?? new Dictionary<string, JsonElement>();
        ModelId = modelId;
        TokenUsage = tokenUsage;
    }
}