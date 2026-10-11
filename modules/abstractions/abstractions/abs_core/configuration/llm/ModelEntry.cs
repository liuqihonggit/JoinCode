
namespace JoinCode.Abstractions.Configuration.Llm;

/// <summary>
/// 模型条目 — 描述一个可用的 LLM 模型
/// </summary>
public sealed class ModelEntry {
    /// <summary>获取模型标识。</summary>
    public string Id { get; }
    /// <summary>获取显示名称。</summary>
    public string DisplayName { get; }
    /// <summary>获取上下文窗口大小。</summary>
    public int ContextWindow { get; }
    /// <summary>获取模型描述。</summary>
    public string Description { get; }

    /// <summary>构造模型条目。</summary>
    /// <param name="id">模型标识。</param>
    /// <param name="displayName">显示名称。</param>
    /// <param name="contextWindow">上下文窗口大小。</param>
    /// <param name="description">模型描述。</param>
    public ModelEntry(string id, string displayName, int contextWindow, string description = "") {
        Id = id;
        DisplayName = displayName;
        ContextWindow = contextWindow;
        Description = description;
    }
}
