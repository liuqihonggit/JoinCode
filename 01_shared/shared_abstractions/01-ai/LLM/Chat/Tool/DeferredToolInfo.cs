namespace JoinCode.Abstractions.LLM.Chat;

public sealed class DeferredToolInfo
{
    public string Name { get; }
    public string? Description { get; }
    public string? InputSchemaJson { get; }
    public bool IsMcp { get; }

    /// <summary>
    /// 主分组名 — 来自 [McpToolDispatch(ToolCategory.Xxx)] 特性标记
    /// </summary>
    public string? Category { get; }

    /// <summary>
    /// 子分组名 — 二级分组
    /// </summary>
    public string? GroupName { get; }

    public DeferredToolInfo(string name, string? description = null, string? inputSchemaJson = null, bool isMcp = false, string? category = null, string? groupName = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description;
        InputSchemaJson = inputSchemaJson;
        IsMcp = isMcp;
        Category = category;
        GroupName = groupName;
    }
}
