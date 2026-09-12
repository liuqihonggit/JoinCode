
namespace McpToolDispatch;

/// <summary>
/// 基于源码生成器的工具分类提供者实现
/// </summary>
[Register(typeof(IToolCategoryProvider), ServiceLifetime.Singleton)]
public sealed partial class GeneratedToolCategoryProvider : ServiceEntity, IToolCategoryProvider
{
    public Dictionary<string, List<ToolCategoryEntry>> GetAvailableToolCategories()
    {
        return GeneratedToolHandlerRegistration_JoinCode_McpToolDispatch.GetAvailableToolCategories();
    }

    public Dictionary<string, List<ToolCategoryEntry>> GetVisibleToolCategories()
    {
        return GeneratedToolHandlerRegistration_JoinCode_McpToolDispatch.GetVisibleToolCategories();
    }
}
