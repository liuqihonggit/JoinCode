
namespace Core.DependencyInjection;

public static partial class ServiceRegistration
{
    /// <summary>
    /// 注册代码索引服务（委托给 <c>JoinCode.Eyes.DependencyInjection.ServiceRegistration.AddCodeIndexServices</c>）。
    /// </summary>
    /// <param name="services">DI 容器。</param>
    /// <param name="workspaceRoot">工作区根目录路径。</param>
    /// <returns>已注册服务的 <see cref="IServiceCollection"/> 实例。</returns>
    public static IServiceCollection AddCodeIndexServices(this IServiceCollection services, string workspaceRoot)
    {
        global::JoinCode.Eyes.DependencyInjection.ServiceRegistration.AddCodeIndexServices(services, workspaceRoot);

        return services;
    }
}
