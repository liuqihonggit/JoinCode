
namespace Core.Memdir;

public static class MemdirDependencyInjectionExtensions
{
    public static IServiceCollection AddMemdirServices(this IServiceCollection services, Func<IServiceProvider, string>? storagePathFactory = null)
    {
        // MemdirOptions — [Register] 自动注册（DI 构造函数从 WorkflowConfig 获取 StoragePath）
        // 若提供 storagePathFactory，则覆盖自动注册的实例
        if (storagePathFactory is not null)
        {
            services.AddSingleton(sp =>
            {
                var storagePath = storagePathFactory(sp);
                return new MemdirOptions { StoragePath = storagePath };
            });
        }

        // MemoryOptionalServices — [Register] 自动注册（构造函数参数均为可选 DI 接口）

        return services;
    }
}
