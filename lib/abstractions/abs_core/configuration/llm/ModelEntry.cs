
namespace JoinCode.Abstractions.Configuration.Llm;

/// <summary>
/// 模型条目 — 描述一个可用的 LLM 模型
/// </summary>
public sealed class ModelEntry
{
    public string Id { get; }
    public string DisplayName { get; }
    public int ContextWindow { get; }
    public string Description { get; }

    public ModelEntry(string id, string displayName, int contextWindow, string description = "")
    {
        Id = id;
        DisplayName = displayName;
        ContextWindow = contextWindow;
        Description = description;
    }
}
