namespace Plugins.Browser;

/// <summary>
/// Browser 插件 DI 注册扩展 — 替代 NoOpBrowserAutomationService
/// 在 Host 的 Program.cs 中调用 services.AddBrowserAutomation() 启用
/// </summary>
public static class BrowserServiceExtensions
{
    /// <summary>
    /// 注册 PuppeteerSharp 浏览器自动化服务，替代默认的 NoOp 实现
    /// </summary>
    public static IServiceCollection AddBrowserAutomation(this IServiceCollection services)
    {
        // PuppeteerBrowserAutomationService 已通过 [Register] 自动注册
        // 移除 NoOp 等其他默认注册，使 Puppeteer 实现生效
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IBrowserAutomationService)
            && d.ImplementationType != typeof(PuppeteerBrowserAutomationService));
        if (descriptor is not null)
        {
            services.Remove(descriptor);
        }

        return services;
    }
}
