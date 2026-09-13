namespace JoinCode.Hands.DependencyInjection;

/// <summary>
/// 服务注册入口 — 提供 API 客户端、代码安全、技能等模块的 DI 扩展方法。
/// </summary>
public static partial class ServiceRegistration
{
    /// <summary>
    /// 注册 API 客户端相关服务。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>用于链式调用的服务集合。</returns>
    public static IServiceCollection AddApiClientServices(this IServiceCollection services)
    {
        services.AddOptions<ApiSettings>();
        services.AddSingleton(new VcrOptions());
        return services;
    }

    /// <summary>
    /// 注册代码安全相关服务。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>用于链式调用的服务集合。</returns>
    public static IServiceCollection AddCodeSecurityServices(this IServiceCollection services)
    {
        return services;
    }

    /// <summary>
    /// 注册技能相关服务。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <returns>用于链式调用的服务集合。</returns>
    public static IServiceCollection AddSkillServices(this IServiceCollection services)
    {
        return services;
    }
}
