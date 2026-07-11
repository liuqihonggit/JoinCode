namespace JoinCode.Browser.DependencyInjection;

/// <summary>
/// Browser DI 注册
/// </summary>
public static partial class ServiceRegistration
{
    /// <summary>
    /// 注册 PuppeteerSharp 浏览器自动化服务，替代默认的 NoOp 实现
    /// </summary>
    public static IServiceCollection AddBrowserServices(this IServiceCollection services)
    {
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IBrowserAutomationService)
            && d.ImplementationType != typeof(global::Plugins.Browser.PuppeteerBrowserAutomationService));
        if (descriptor is not null)
        {
            services.Remove(descriptor);
        }

        return services;
    }
}
