namespace JoinCode.Brain.DependencyInjection;

/// <summary>
/// 服务注册入口，提供提示与上下文压缩相关服务的 DI 扩展方法
/// </summary>
public static partial class ServiceRegistration {
    /// <summary>
    /// 注册提示相关服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddPromptServices(this IServiceCollection services) {
        return services;
    }

    /// <summary>
    /// 注册上下文压缩相关服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合</returns>
    public static IServiceCollection AddContextCompressionServices(this IServiceCollection services) {
        return services;
    }
}