namespace JoinCode.Guard.DependencyInjection;

/// <summary>
/// 守护服务注册 — 提供 DI 扩展方法以注册权限、安全、配置、钩子等守护服务
/// </summary>
public static partial class ServiceRegistration {
    /// <summary>
    /// 注册全部守护服务(权限、权限钩子、安全、配置、钩子系统)
    /// </summary>
    public static IServiceCollection AddGuardServices(this IServiceCollection services) {
        services.AddPermissionServices();
        services.AddPermissionHookServices();
        services.AddSecurityServices();
        services.AddConfigurationServices();
        services.AddHookSystem();
        return services;
    }

    /// <summary>
    /// 注册安全服务(沙箱 IPC 客户端、沙箱管理器)
    /// </summary>
    public static IServiceCollection AddSecurityServices(this IServiceCollection services) {
        // SandboxProvider 由 SandboxProvidersPlugin 运行时注册(ADR 0098 万物皆插件)
        services.TryAddSingleton(typeof(SandboxIpcClient), sp => new SandboxIpcClient(
            sp.GetRequiredService<IProcessService>(),
            sp.GetRequiredService<IFileSystem>(),
            sp.GetService<ILogger<SandboxIpcClient>>()));
        services.TryAddSingleton<ISandboxManager, SandboxManager>();
        return services;
    }

    /// <summary>
    /// 注册配置服务(模型配置加载器、供应商定义注册表、统一配置入口 IConfigRoot)
    /// </summary>
    public static IServiceCollection AddConfigurationServices(this IServiceCollection services) {
        services.TryAddSingleton<IModelConfigLoader, ModelConfigLoader>();
        services.TryAddSingleton<IProviderDefinitionRegistry>(sp => Core.Configuration.Providers.ProviderDefinitionRegistry.Create(
            sp.GetRequiredService<IModelConfigLoader>(),
            sp.GetService<IFileSystem>(),
            sp.GetService<ILogger<Core.Configuration.Providers.ProviderDefinitionRegistry>>()));
        services.TryAddSingleton<IMemoryCache>(sp => new MemoryCache(new MemoryCacheOptions {
            SizeLimit = 4096,
            CompactionPercentage = 0.25,
            ExpirationScanFrequency = TimeSpan.FromMinutes(5),
        }));
        services.TryAddSingleton<IProjectLocator, ProjectLocator>();
        services.TryAddSingleton<IConfigRoot>(sp => {
            var fs = sp.GetRequiredService<IFileSystem>();
            var cache = sp.GetRequiredService<IMemoryCache>();
            var locator = sp.GetRequiredService<IProjectLocator>();
            var logger = sp.GetService<ILogger<UnifiedConfigRoot>>();

            SettingsJson? userSettings = null;
            SettingsJson? proSettings = null;
            SettingsJson? localSettings = null;
            try {
                userSettings = SettingsLoader.LoadUserSettings(fs).GetAwaiter().GetResult();
                if (locator.TryLocateProjectRoot(out var projectRoot)) {
                    proSettings = SettingsLoader.LoadProjectSettingsAsync(fs, projectRoot).GetAwaiter().GetResult();
                    localSettings = SettingsLoader.LoadLocalSettingsAsync(fs, projectRoot).GetAwaiter().GetResult();
                }
            } catch (Exception ex) {
                logger?.LogWarning(ex, "Failed to load settings for IConfigRoot, falling back to empty layers");
            }

            var user = new ConfigLayer(userSettings);
            var pro = new ConfigLayer(proSettings);
            var local = new ConfigLayer(localSettings);
            return new UnifiedConfigRoot(user, pro, local, cache);
        });
        return services;
    }

    /// <summary>
    /// 注册权限服务(权限配置选项,从默认值与 settings.json 加载)
    /// </summary>
    public static IServiceCollection AddPermissionServices(this IServiceCollection services) {
        services.AddOptions<PermissionConfig>()
            .Configure<IFileSystem, ILogger<PermissionConfig>>((options, fs, logger) => {
                var defaultConfig = PermissionConfig.CreateDefault();
                options.AutoApprovedTools = defaultConfig.AutoApprovedTools;
                options.AutoRejectedTools = defaultConfig.AutoRejectedTools;
                options.DangerousOperationPatterns = defaultConfig.DangerousOperationPatterns;
                options.WriteOperationPatterns = defaultConfig.WriteOperationPatterns;
                options.ReadOperationPatterns = defaultConfig.ReadOperationPatterns;
                options.ShellOperationPatterns = defaultConfig.ShellOperationPatterns;
                options.SensitivePathPatterns = defaultConfig.SensitivePathPatterns;
                options.DangerousCommandPatterns = defaultConfig.DangerousCommandPatterns;

                // IFileSystem 异步化后 fire-and-forget 加载；PhysicalFileSystem UTF-8 走 mmap 同步完成，实际不阻塞
                _ = LoadPermissionsFromSettingsAsync(options, fs, logger);
            });

        return services;
    }

    /// <summary>
    /// 注册钩子系统(钩子执行器工厂、钩子配置管理器及多来源 JSON 配置提供器)
    /// </summary>
    public static IServiceCollection AddHookSystem(this IServiceCollection services) {
        services.TryAddSingleton<IHookExecutorFactory>(sp => {
            var factory = new HookExecutorFactory(sp.GetService<ILogger<HookExecutorFactory>>());
            foreach (var executor in sp.GetServices<IHookExecutor>())
                factory.RegisterExecutor(executor);
            return factory;
        });

        services.TryAddSingleton<IHookConfigurationManager>(sp => {
            var fs = sp.GetRequiredService<IFileSystem>();
            var manager = new HookConfigurationManager(fs, sp.GetService<ILogger<HookConfigurationManager>>());
            var logger = sp.GetService<ILogger<JsonFileHookConfigurationProvider>>();

            // 路径委托 SettingsLoader 主链路三层解析（JCC_SETTINGS_FILE_NAME/JCC_APP_DATA_FOLDER 覆盖行为与主链路一致）
            var userSettingsPath = SettingsLoader.GetUserSettingsPath();
            manager.RegisterProvider(HookSource.UserSettings,
                new JsonFileHookConfigurationProvider(userSettingsPath, HookSource.UserSettings, fs, logger));

            var projectSettingsPath = SettingsLoader.GetProjectSettingsPath(fs.GetCurrentDirectory());
            manager.RegisterProvider(HookSource.ProjectSettings,
                new JsonFileHookConfigurationProvider(projectSettingsPath, HookSource.ProjectSettings, fs, logger));

            var localSettingsPath = SettingsLoader.GetLocalSettingsPath(fs.GetCurrentDirectory());
            manager.RegisterProvider(HookSource.LocalSettings,
                new JsonFileHookConfigurationProvider(localSettingsPath, HookSource.LocalSettings, fs, logger));

            // 策略源 — managed-settings.json 顶层 hooks（对齐 TS policySettings 参与钩子合并；
            // PolicySettings 不可编辑(IsEditable=false)，JsonFileHookConfigurationProvider 写回被守卫拒绝）
            manager.RegisterProvider(HookSource.PolicySettings,
                new JsonFileHookConfigurationProvider(SettingsLoader.GetManagedSettingsPath(), HookSource.PolicySettings, fs, logger));

            return manager;
        });

        return services;
    }

    /// <summary>
    /// 注册权限钩子服务
    /// </summary>
    public static IServiceCollection AddPermissionHookServices(this IServiceCollection services) {
        return services;
    }

    /// <summary>
    /// 权限规则加载 — 委托 <see cref="PermissionRulesFileLoader"/>（TASK070 P1-P2 唯一数据源：
    /// 启动加载与运行期热加载共用同一实现，消除双处维护）。
    /// </summary>
    private static Task LoadPermissionsFromSettingsAsync(PermissionConfig options, IFileSystem fs, ILogger? logger = null) =>
        PermissionRulesFileLoader.LoadAsync(options, fs, logger);
}