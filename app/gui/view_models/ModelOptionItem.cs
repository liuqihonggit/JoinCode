namespace JoinCode.Gui.ViewModels;

/// <summary>
/// 模型下拉展示项 — 区分供应商与模型（如 "OpenAI · GPT-4o"、"OpenAI · sensenova-6.7-flash-lite"）。
/// <c>Id</c> 是底层模型 ID（写回共享配置用），<c>DisplayText</c> 是下拉可见文本。
/// <c>ModalityTags</c> 是模态能力标签文本（如 "📷🖼🔧"），用于下拉项辅助展示。
/// </summary>
public sealed class ModelOptionItem
{
    /// <summary>模型 ID（写入共享配置的真实标识）</summary>
    public string Id { get; }

    /// <summary>下拉展示文本（供应商显示名 · 模型显示名）</summary>
    public string DisplayText { get; }

    /// <summary>模态能力标签文本（如 "📷🖼🔧"），空字符串表示仅文本</summary>
    public string ModalityTags { get; }

    /// <summary>创建展示项</summary>
    public ModelOptionItem(string id, string displayText, string modalityTags = "")
    {
        Id = id;
        DisplayText = displayText;
        ModalityTags = modalityTags;
    }

    public override bool Equals(object? obj)
        => obj is ModelOptionItem other && string.Equals(Id, other.Id, StringComparison.OrdinalIgnoreCase);

    public override int GetHashCode() => StringComparer.OrdinalIgnoreCase.GetHashCode(Id);
}
