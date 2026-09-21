namespace JoinCode.Abstractions.LLM.Chat;

public sealed class CacheSafeParams {
    /// <summary>获取渲染后的系统提示。</summary>
    public string? RenderedSystemPrompt { get; init; }
    /// <summary>获取模型标识。</summary>
    public string? ModelId { get; init; }
    /// <summary>获取工具名称列表。</summary>
    public IReadOnlyList<string> ToolNames { get; init; } = [];
    /// <summary>获取用户上下文字典。</summary>
    public Dictionary<string, string> UserContext { get; init; } = [];
    /// <summary>获取系统上下文字典。</summary>
    public Dictionary<string, string> SystemContext { get; init; } = [];
    /// <summary>获取内容替换状态。</summary>
    public ContentReplacementState? ContentReplacementState { get; init; }

    /// <summary>克隆当前实例。</summary>
    public CacheSafeParams Clone() {
        return new CacheSafeParams {
            RenderedSystemPrompt = RenderedSystemPrompt,
            ModelId = ModelId,
            ToolNames = ToolNames,
            UserContext = new Dictionary<string, string>(UserContext),
            SystemContext = new Dictionary<string, string>(SystemContext),
            ContentReplacementState = ContentReplacementState?.Clone()
        };
    }
}