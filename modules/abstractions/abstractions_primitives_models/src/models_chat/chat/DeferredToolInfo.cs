namespace JoinCode.Abstractions.LLM.Chat;

/// <summary>延迟加载工具信息。</summary>
public sealed class DeferredToolInfo {
    /// <summary>获取工具名称。</summary>
    public string Name { get; }
    /// <summary>获取工具描述。</summary>
    public string? Description { get; }
    /// <summary>获取输入 schema 的 JSON 字符串。</summary>
    public string? InputSchemaJson { get; }
    /// <summary>获取是否为 MCP 工具。</summary>
    public bool IsMcp { get; }

    /// <summary>
    /// 主分组名 — 来自 [McpToolDispatch(ToolCategory.Xxx)] 特性标记
    /// </summary>
    public string? Category { get; }

    /// <summary>
    /// 子分组名 — 二级分组
    /// </summary>
    public string? GroupName { get; }

    /// <summary>
    /// 构造延迟加载工具信息。
    /// </summary>
    /// <param name="name">工具名称。</param>
    /// <param name="description">工具描述。</param>
    /// <param name="inputSchemaJson">输入 schema 的 JSON 字符串。</param>
    /// <param name="isMcp">是否为 MCP 工具。</param>
    /// <param name="category">主分组名。</param>
    /// <param name="groupName">子分组名。</param>
    public DeferredToolInfo(string name, string? description = null, string? inputSchemaJson = null, bool isMcp = false, string? category = null, string? groupName = null) {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Description = description;
        InputSchemaJson = inputSchemaJson;
        IsMcp = isMcp;
        Category = category;
        GroupName = groupName;
    }
}
