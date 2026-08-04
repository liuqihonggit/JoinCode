
namespace McpToolDispatch;

/// <summary>
/// 基于源码生成器的工具分类提供者实现
/// </summary>
[Register]
public sealed partial class GeneratedToolCategoryProvider : IToolCategoryProvider
{
    public Dictionary<string, List<ToolCategoryEntry>> GetAvailableToolCategories()
    {
        return GeneratedToolHandlerRegistration_JoinCode_McpToolDispatch.GetAvailableToolCategories();
    }
}
