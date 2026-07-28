
namespace McpToolHandlers;

/// <summary>
/// 基于源码生成器的工具分类提供者实现
/// </summary>
[Register]
public sealed partial class GeneratedToolCategoryProvider : IToolCategoryProvider
{
    /// <summary>
    /// 获取可用工具的分类信息
    /// </summary>
    public Dictionary<string, List<(string Name, string Description)>> GetAvailableToolCategories()
    {
        return GeneratedToolHandlerRegistration_JoinCode_McpToolHandlers.GetAvailableToolCategories();
    }
}
