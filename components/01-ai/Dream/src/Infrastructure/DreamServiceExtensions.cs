
namespace JoinCode.Dream;

/// <summary>
/// 做梦系统服务注册扩展
/// </summary>
public static class DreamServiceExtensions
{
    /// <summary>
    /// 添加做梦系统服务（内存存储）
    /// </summary>
    public static IServiceCollection AddDreamSystem(
        this IServiceCollection services,
        Action<AutoDreamConfig>? configureOptions = null)
    {
        // 注册配置（AutoDreamConfig 无 [Register]，需手动注册）
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.AddSingleton(new AutoDreamConfig());
        }

        // 以下服务已通过 [Register] 特性自动注册：
        // ISessionScanner → DefaultSessionScanner
        // IChatCompletionClient → ChatCompletionClient
        // IDreamFeature → DreamFeature
        // IDreamTaskRegistry → InMemoryDreamTaskRegistry（内存版，覆盖 PersistentDreamTaskRegistry）

        return services;
    }

    /// <summary>
    /// 添加做梦系统服务（持久化存储）
    /// </summary>
    public static IServiceCollection AddDreamSystemWithPersistence(
        this IServiceCollection services,
        Action<AutoDreamConfig>? configureOptions = null)
    {
        // 注册配置（AutoDreamConfig 无 [Register]，需手动注册）
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.AddSingleton(new AutoDreamConfig());
        }

        // 以下服务已通过 [Register] 特性自动注册：
        // ISessionScanner → DefaultSessionScanner
        // IDreamTaskPersistence → JsonFileDreamTaskPersistence
        // IDreamTaskRegistry → PersistentDreamTaskRegistry
        // IChatCompletionClient → ChatCompletionClient
        // IDreamFeature → DreamFeature

        return services;
    }

    /// <summary>
    /// 初始化做梦系统
    /// </summary>
    public static IServiceProvider InitializeDreamSystem(this IServiceProvider serviceProvider)
    {
        // DreamFeature 已注册为单例，不需要单独初始化
        return serviceProvider;
    }

    /// <summary>
    /// 初始化做梦系统（持久化存储）
    /// </summary>
    public static async Task<IServiceProvider> InitializeDreamSystemWithPersistenceAsync(
        this IServiceProvider serviceProvider)
    {
        // 加载活跃任务
        if (serviceProvider.GetService<IDreamTaskRegistry>() is PersistentDreamTaskRegistry registry)
        {
            await registry.LoadActiveTasksAsync().ConfigureAwait(false);
        }

        return serviceProvider;
    }
}
