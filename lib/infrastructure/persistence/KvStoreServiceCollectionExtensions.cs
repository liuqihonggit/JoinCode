namespace Infrastructure.Persistence;

/// <summary>
/// KV 存储服务注册扩展 — 提供 AddPithosKvStore/AddInMemoryKvStore DI 扩展方法。
/// </summary>
public static class KvStoreServiceCollectionExtensions {

    /// <summary>
    /// 注册 PithosDB LSM-Tree KV 存储 — 磁盘持久化,支持增量写入和崩溃恢复。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="dataDirectory">数据目录路径。</param>
    /// <param name="lifetime">服务生命周期(默认 Singleton)。</param>
    /// <returns>服务集合(链式调用)。</returns>
    public static IServiceCollection AddPithosKvStore(
        this IServiceCollection services,
        string dataDirectory,
        Microsoft.Extensions.DependencyInjection.ServiceLifetime lifetime = Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton) {
        services.Add(new ServiceDescriptor(typeof(IKvStore), sp => new PithosKvStore(dataDirectory), lifetime));
        return services;
    }

    /// <summary>
    /// 注册内存 KV 存储 — 纯内存,0 磁盘 IO,用于测试或临时数据。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="lifetime">服务生命周期(默认 Singleton)。</param>
    /// <returns>服务集合(链式调用)。</returns>
    public static IServiceCollection AddInMemoryKvStore(
        this IServiceCollection services,
        Microsoft.Extensions.DependencyInjection.ServiceLifetime lifetime = Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton) {
        services.Add(new ServiceDescriptor(typeof(IKvStore), sp => new InMemoryKvStore(), lifetime));
        return services;
    }
}
