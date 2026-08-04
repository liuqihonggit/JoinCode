namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 工具分类提供者接口
/// </summary>
public interface IToolCategoryProvider
{
    /// <summary>
    /// 获取所有工具的分类信息（含 OnError 工具）
    /// </summary>
    Dictionary<string, List<ToolCategoryEntry>> GetAvailableToolCategories();

    /// <summary>
    /// 获取可见工具的分类信息（排除 OnError 工具，用于系统提示词）
    /// </summary>
    Dictionary<string, List<ToolCategoryEntry>> GetVisibleToolCategories();
}

/// <summary>
/// 工具分类条目 — 描述单个工具的分类、类型和分组信息
/// </summary>
public sealed record ToolCategoryEntry
{
    public required string Name { get; init; }
    public required string Description { get; init; }
    public required ToolKind Kind { get; init; }
    public string? GroupName { get; init; }
}
