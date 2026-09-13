namespace JoinCode.Vault.DependencyInjection;

/// <summary>
/// Vault 服务注册入口 — 提供 DI 扩展方法
/// </summary>
public static partial class ServiceRegistration
{
    /// <summary>
    /// 注册 Vault 全部服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="storagePathFactory">可选的存储路径工厂,按服务提供商动态生成路径</param>
    /// <returns>服务集合,便于链式调用</returns>
    public static IServiceCollection AddVaultServices(this IServiceCollection services, Func<IServiceProvider, string>? storagePathFactory = null)
    {
        services.AddMemdirServices(storagePathFactory);
        return services;
    }

    /// <summary>
    /// 注册 Memdir 相关服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="storagePathFactory">可选的存储路径工厂,按服务提供商动态生成路径</param>
    /// <returns>服务集合,便于链式调用</returns>
    public static IServiceCollection AddMemdirServices(this IServiceCollection services, Func<IServiceProvider, string>? storagePathFactory = null)
    {
        if (storagePathFactory is not null)
        {
            services.AddSingleton(sp =>
            {
                var storagePath = storagePathFactory(sp);
                return new MemdirOptions { StoragePath = storagePath };
            });
        }

        return services;
    }
}
