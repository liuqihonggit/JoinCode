namespace Core.Configuration;

public static class ConfigurationDependencyInjectionExtensions
{
    public static IServiceCollection AddConfigurationServices(this IServiceCollection services)
    {
        // IFastModeService — [Register] 自动注册（FastModeService）
        // IBriefModeService — [Register] 自动注册（BriefModeService）

        // 设置变更管道 — 由 [RegisterMiddleware] + 生成器自动注册

        return services;
    }
}
