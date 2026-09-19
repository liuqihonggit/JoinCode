namespace JoinCode.Scheduling.DependencyInjection;

/// <summary>
/// 调度服务注册入口 — 提供 DI 容器扩展方法用于注册调度相关服务
/// </summary>
public static partial class ServiceRegistration {
    /// <summary>
    /// 注册调度服务（包含 Cron 服务）
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>服务集合以便链式调用</returns>
    public static IServiceCollection AddSchedulingServices(this IServiceCollection services) {
        services.AddCronServices();
        return services;
    }

    /// <summary>
    /// 注册 Cron 调度相关服务
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <param name="tasksDirectory">任务存储目录路径，为 null 时使用默认目录</param>
    /// <returns>服务集合以便链式调用</returns>
    public static IServiceCollection AddCronServices(this IServiceCollection services, string? tasksDirectory = null) {
        return services;
    }
}