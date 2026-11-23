namespace JoinCode.Plugins.Infrastructure.Services;

/// <summary>
/// 插件服务 DI 注册扩展 — 注册动态插件相关服务到 DI 容器
/// </summary>
public static class PluginServiceRegistration {
    /// <summary>
    /// 注册动态插件服务 — PluginApprovalRegistry + DynamicPluginRegistry + PluginFileWatcher
    /// 外部插件通过 YAML 配置 + 文件监控加载，handler 由 IAotHandlerRegistry（源码生成器）提供
    /// </summary>
    public static IServiceCollection AddDynamicPluginServices(this IServiceCollection services) {
        services.AddSingleton<PluginApprovalRegistry>();

        services.AddSingleton<DynamicPluginRegistry>(sp => {
            var approval = sp.GetRequiredService<PluginApprovalRegistry>();
            var aotRegistry = sp.GetService<IAotHandlerRegistry>();
            return new DynamicPluginRegistry(approval, aotRegistry: aotRegistry);
        });

        services.AddSingleton<PluginFileWatcher>();

        return services;
    }
}
