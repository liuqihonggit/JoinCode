
namespace McpToolDispatch;

/// <summary>
/// 基于源码生成器的工具分类提供者实现
/// </summary>
[Register(typeof(IToolCategoryProvider), ServiceLifetime.Singleton)]
public sealed partial class GeneratedToolCategoryProvider : ServiceEntity, IToolCategoryProvider {
    /// <summary>
    /// 获取所有可用工具分类 — 委托源码生成器生成的注册方法，返回分组名到分类条目列表的字典
    /// </summary>
    /// <returns>分组名到工具分类条目列表的字典</returns>
    public Dictionary<string, List<ToolCategoryEntry>> GetAvailableToolCategories() {
        return GeneratedToolHandlerRegistration_JoinCode_McpToolDispatch.GetAvailableToolCategories();
    }

    /// <summary>
    /// 获取所有可见工具分类 — 委托源码生成器生成的注册方法，过滤隐藏分类后返回
    /// </summary>
    /// <returns>分组名到可见工具分类条目列表的字典</returns>
    public Dictionary<string, List<ToolCategoryEntry>> GetVisibleToolCategories() {
        return GeneratedToolHandlerRegistration_JoinCode_McpToolDispatch.GetVisibleToolCategories();
    }
}