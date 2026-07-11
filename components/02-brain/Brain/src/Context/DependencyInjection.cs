namespace Core.DependencyInjection;

/// <summary>
/// Brain/Context 子系统的 DI 注册扩展方法
/// </summary>
public static class ContextCompressionServiceRegistration
{
    public static IServiceCollection AddContextCompressionServices(this IServiceCollection services)
    {
        // Options — 由 [RegisterOptions] + 生成器自动注册: ContextCompressionConfig, ContextHierarchyOptions, CompressionOptions, ReferenceResolutionOptions, CompactThresholds

        // CodeContentCompressor, DialogueCompressor, ReferenceIndexCompressor,
        // ContextCompressor, ContextLayer, ContextHierarchy, ReferenceResolver,
        // SessionStats, FileSessionMetaStore, AutoCompactService — auto-registered via [Register]

        return services;
    }
}
