namespace JoinCode.Abstractions.LLM.Chat;

public sealed class ToolSpec {
    /// <summary>获取工具名称。</summary>
    public string Name { get; }
    /// <summary>获取工具描述。</summary>
    public string? Description { get; }
    /// <summary>获取输入 Schema JSON。</summary>
    public string? InputSchemaJson { get; }

    /// <summary>
    /// 主分组名 — 两级分组导航: map[主分组][子分组][工具名]
    /// </summary>
    public string? Category { get; }

    /// <summary>
    /// 子分组名 — 二级分组
    /// </summary>
    public string? GroupName { get; }

    /// <summary>构造工具规格。</summary>
    /// <param name="name">工具名称。</param>
    /// <param name="description">工具描述。</param>
    /// <param name="inputSchemaJson">输入 Schema JSON。</param>
    /// <param name="category">主分组名。</param>
    /// <param name="groupName">子分组名。</param>
    public ToolSpec(string name, string? description = null, string? inputSchemaJson = null, string? category = null, string? groupName = null) {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description;
        InputSchemaJson = inputSchemaJson;
        Category = category;
        GroupName = groupName;
    }
}