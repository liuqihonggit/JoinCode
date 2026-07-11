
namespace Core.Hooks;

public static class HookDependencyInjectionExtensions
{
    public static IServiceCollection AddHookSystem(this IServiceCollection services)
    {
        // 工厂方法：从 DI 解析所有执行器并注册到 HookExecutorFactory（集合聚合，需手动注册）
        services.TryAddSingleton<IHookExecutorFactory>(sp =>
        {
            var factory = new HookExecutorFactory(sp.GetService<ILogger<HookExecutorFactory>>());
            foreach (var executor in sp.GetServices<IHookExecutor>())
                factory.RegisterExecutor(executor);
            return factory;
        });

        // 工厂方法：创建 HookConfigurationManager 并注册文件配置提供者（复杂初始化，需手动注册）
        services.TryAddSingleton<IHookConfigurationManager>(sp =>
        {
            var fs = sp.GetRequiredService<IFileSystem>();
            var manager = new HookConfigurationManager(fs, sp.GetService<ILogger<HookConfigurationManager>>());
            var logger = sp.GetService<ILogger<JsonFileHookConfigurationProvider>>();

            // 用户全局设置: ~/.jcc/settings.json
            var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var userSettingsPath = Path.Combine(appDataRoot, AppDataConstants.AppDataFolder, AppDataConstants.SettingsFileName);
            manager.RegisterProvider(HookSource.UserSettings,
                new JsonFileHookConfigurationProvider(userSettingsPath, HookSource.UserSettings, fs, logger));

            // 项目设置: .jcc/settings.json
            var projectSettingsPath = Path.Combine(fs.GetCurrentDirectory(), AppDataConstants.AppDataFolder, AppDataConstants.SettingsFileName);
            manager.RegisterProvider(HookSource.ProjectSettings,
                new JsonFileHookConfigurationProvider(projectSettingsPath, HookSource.ProjectSettings, fs, logger));

            // 项目本地设置: .jcc/settings.local.json
            var localSettingsPath = Path.Combine(fs.GetCurrentDirectory(), AppDataConstants.AppDataFolder, "settings.local.json");
            manager.RegisterProvider(HookSource.LocalSettings,
                new JsonFileHookConfigurationProvider(localSettingsPath, HookSource.LocalSettings, fs, logger));

            return manager;
        });

        return services;
    }

    public static IServiceCollection AddPermissionHookServices(this IServiceCollection services)
    {
        return services;
    }
}
