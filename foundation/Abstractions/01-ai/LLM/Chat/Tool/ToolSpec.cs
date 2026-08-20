namespace JoinCode.Abstractions.LLM.Chat;

public sealed class ToolSpec
{
    public string Name { get; }
    public string? Description { get; }
    public string? InputSchemaJson { get; }

    /// <summary>
    /// 主分组名 — 两级分组导航: map[主分组][子分组][工具名]
    /// </summary>
    public string? Category { get; }

    /// <summary>
    /// 子分组名 — 二级分组
    /// </summary>
    public string? GroupName { get; }

    public ToolSpec(string name, string? description = null, string? inputSchemaJson = null, string? category = null, string? groupName = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description;
        InputSchemaJson = inputSchemaJson;
        Category = category;
        GroupName = groupName;
    }
}
