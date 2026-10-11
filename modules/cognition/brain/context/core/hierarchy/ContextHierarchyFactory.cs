namespace Core.Context;

/// <summary>
/// 上下文层级工厂，按抽象层配置创建 ContextHierarchy 实例
/// </summary>
[Register(typeof(IContextHierarchyFactory), ServiceLifetime.Singleton)]
public sealed partial class ContextHierarchyFactory : ServiceEntity, IContextHierarchyFactory {

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <param name="logger">日志记录器（可选）</param>
    public ContextHierarchyFactory(ILogger<ContextHierarchyFactory>? logger = null) {
        _logger = logger;
    }
    private readonly ILogger<ContextHierarchyFactory>? _logger;

    /// <summary>
    /// 显式接口实现：按抽象层选项创建上下文层级实例
    /// </summary>
    /// <param name="options">上下文层级配置选项</param>
    /// <returns>新建的上下文层级实例</returns>
    JoinCode.Abstractions.Brain.Context.Hierarchy.IContextHierarchy IContextHierarchyFactory.Create(JoinCode.Abstractions.Brain.Context.Hierarchy.ContextHierarchyOptions options) {
        var brainOptions = new ContextHierarchyOptions {
            TokenThreshold = options.TokenThreshold,
            AutoCompressionEnabled = options.AutoCompressionEnabled,
            MaxLayers = options.MaxLayers,
            DefaultCompressionRatio = options.DefaultCompressionRatio
        };
        return ContextHierarchy.Create(brainOptions, _logger as ILogger<ContextHierarchy>);
    }
}