namespace JoinCode.Abstractions.Tools;

/// <summary>
/// 工具分类提供者接口，用于解耦 PromptConfig 对 McpToolHandlers 的直接依赖
/// </summary>
public interface IToolCategoryProvider
{
    /// <summary>
    /// 获取可用工具的分类信息
    /// </summary>
    /// <returns>分类名称到工具列表的映射</returns>
    Dictionary<string, List<(string Name, string Description)>> GetAvailableToolCategories();
}
