namespace Core.DependencyInjection;

/// <summary>
/// Hands/Api 子系统的 DI 注册扩展方法
/// </summary>
public static class ApiClientServiceRegistration
{
    public static IServiceCollection AddApiClientServices(this IServiceCollection services)
    {
        services.AddOptions<ApiSettings>();

        // RetryPolicyOptions — [Register] 自动注册（从 IOptions<ApiSettings> 构造）
        // RetryPolicy — [Register] 自动注册（构造函数 RetryPolicyOptions? 可选）
        // CostTracker/ICostTracker — [Register] 自动注册（storagePath 可选，默认 AppContext.BaseDirectory）

        // IUsageTracker — [Register] 自动注册（UsageTracker）

        // IRateLimitTracker — [Register] 自动注册（RateLimitTracker）

        // IApiClient — [Register] 自动注册（ApiClient DI 构造函数从 IOptions<ApiSettings> 推导）

        return services;
    }
}
