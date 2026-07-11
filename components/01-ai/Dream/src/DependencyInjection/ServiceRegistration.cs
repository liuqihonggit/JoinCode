namespace JoinCode.Dream.DependencyInjection;

/// <summary>
/// Dream DI 注册
/// </summary>
public static partial class ServiceRegistration
{
    /// <summary>
    /// 添加做梦系统服务（内存存储）
    /// </summary>
    public static IServiceCollection AddDreamServices(
        this IServiceCollection services,
        Action<AutoDreamConfig>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.AddSingleton(new AutoDreamConfig());
        }

        return services;
    }

    /// <summary>
    /// 添加做梦系统服务（持久化存储）
    /// </summary>
    public static IServiceCollection AddDreamServicesWithPersistence(
        this IServiceCollection services,
        Action<AutoDreamConfig>? configureOptions = null)
    {
        if (configureOptions != null)
        {
            services.Configure(configureOptions);
        }
        else
        {
            services.AddSingleton(new AutoDreamConfig());
        }

        return services;
    }

    /// <summary>
    /// 初始化做梦系统
    /// </summary>
    public static IServiceProvider InitializeDreamSystem(this IServiceProvider serviceProvider)
    {
        return serviceProvider;
    }

    /// <summary>
    /// 初始化做梦系统（持久化存储）
    /// </summary>
    public static async Task<IServiceProvider> InitializeDreamSystemWithPersistenceAsync(
        this IServiceProvider serviceProvider)
    {
        if (serviceProvider.GetService<IDreamTaskRegistry>() is PersistentDreamTaskRegistry registry)
        {
            await registry.LoadActiveTasksAsync().ConfigureAwait(false);
        }

        return serviceProvider;
    }

    /// <summary>
    /// 添加 Dream 插件服务
    /// </summary>
    public static IServiceCollection AddDreamPluginServices(this IServiceCollection services)
    {
        services.AddSingleton<AutoDreamConfig>(sp =>
        {
            var config = AutoDreamConfigBuilder.Create()
                .WithMinSessions(2)
                .Build();
            return config;
        });

        return services;
    }
}
