namespace JoinCode.Guard.DependencyInjection;

/// <summary>
/// 守护服务注册 — 提供 DI 扩展方法以注册权限、安全、配置、钩子等守护服务
/// </summary>
public static partial class ServiceRegistration
{
    /// <summary>
    /// 注册全部守护服务(权限、权限钩子、安全、配置、钩子系统)
    /// </summary>
    public static IServiceCollection AddGuardServices(this IServiceCollection services)
    {
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
    public static IServiceCollection AddSecurityServices(this IServiceCollection services)
    {
        // SandboxProvider 由 SandboxProvidersPlugin 运行时注册(ADR 0098 万物皆插件)
        services.TryAddSingleton(typeof(SandboxIpcClient), sp => new SandboxIpcClient(
            sp.GetRequiredService<IProcessService>(),
            sp.GetRequiredService<IFileSystem>(),
            sp.GetService<ILogger<SandboxIpcClient>>()));
        services.TryAddSingleton<ISandboxManager, SandboxManager>();
        return services;
    }

    /// <summary>
    /// 注册配置服务(模型配置加载器、供应商定义注册表)
    /// </summary>
    public static IServiceCollection AddConfigurationServices(this IServiceCollection services)
    {
        services.TryAddSingleton<IModelConfigLoader, ModelConfigLoader>();
        services.TryAddSingleton<IProviderDefinitionRegistry, Core.Configuration.Providers.ProviderDefinitionRegistry>();
        return services;
    }

    /// <summary>
    /// 注册权限服务(权限配置选项,从默认值与 settings.json 加载)
    /// </summary>
    public static IServiceCollection AddPermissionServices(this IServiceCollection services)
    {
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

                LoadPermissionsFromSettings(options, fs, logger);
            });

        return services;
    }

    /// <summary>
    /// 注册钩子系统(钩子执行器工厂、钩子配置管理器及多来源 JSON 配置提供器)
    /// </summary>
    public static IServiceCollection AddHookSystem(this IServiceCollection services)
    {
        services.TryAddSingleton<IHookExecutorFactory>(sp =>
        {
            var factory = new HookExecutorFactory(sp.GetService<ILogger<HookExecutorFactory>>());
            foreach (var executor in sp.GetServices<IHookExecutor>())
                factory.RegisterExecutor(executor);
            return factory;
        });

        services.TryAddSingleton<IHookConfigurationManager>(sp =>
        {
            var fs = sp.GetRequiredService<IFileSystem>();
            var manager = new HookConfigurationManager(fs, sp.GetService<ILogger<HookConfigurationManager>>());
            var logger = sp.GetService<ILogger<JsonFileHookConfigurationProvider>>();

            var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var userSettingsPath = Path.Combine(appDataRoot, AppDataConstants.AppDataFolder, AppDataConstants.SettingsFileName);
            manager.RegisterProvider(HookSource.UserSettings,
                new JsonFileHookConfigurationProvider(userSettingsPath, HookSource.UserSettings, fs, logger));

            var projectSettingsPath = Path.Combine(fs.GetCurrentDirectory(), AppDataConstants.AppDataFolder, AppDataConstants.SettingsFileName);
            manager.RegisterProvider(HookSource.ProjectSettings,
                new JsonFileHookConfigurationProvider(projectSettingsPath, HookSource.ProjectSettings, fs, logger));

            var localSettingsPath = Path.Combine(fs.GetCurrentDirectory(), AppDataConstants.AppDataFolder, "settings.local.json");
            manager.RegisterProvider(HookSource.LocalSettings,
                new JsonFileHookConfigurationProvider(localSettingsPath, HookSource.LocalSettings, fs, logger));

            return manager;
        });

        return services;
    }

    /// <summary>
    /// 注册权限钩子服务
    /// </summary>
    public static IServiceCollection AddPermissionHookServices(this IServiceCollection services)
    {
        return services;
    }

    private static void LoadPermissionsFromSettings(PermissionConfig options, IFileSystem fs, ILogger? logger = null)
    {
        try
        {
            var settings = SettingsLoader.LoadUserSettings(fs);
            if (settings?.Current?.Permissions is null)
                return;

            if (settings.Current.Permissions.Allow is { Count: > 0 })
            {
                foreach (var rule in settings.Current.Permissions.Allow)
                {
                    var parsed = ParsePermissionRuleValue(rule);
                    if (parsed is not null && !options.AutoApprovedTools.Values.Any(r =>
                        string.Equals(r.ToolName, parsed.ToolName, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(r.RuleContent ?? "", parsed.RuleContent ?? "", StringComparison.OrdinalIgnoreCase)))
                    {
                        options.AutoApprovedTools[parsed.ToolName] = parsed;
                    }
                }
            }

            if (settings.Current.Permissions.Deny is { Count: > 0 })
            {
                foreach (var rule in settings.Current.Permissions.Deny)
                {
                    var parsed = ParsePermissionRuleValue(rule);
                    if (parsed is not null)
                        options.AutoRejectedTools[parsed.ToolName] = parsed;
                }
            }

            if (settings.Current.Permissions.Ask is { Count: > 0 })
            {
                foreach (var rule in settings.Current.Permissions.Ask)
                {
                    var parsed = ParsePermissionRuleValue(rule);
                    if (parsed is not null)
                        options.AskRules.Add(parsed);
                }
            }

            if (settings.Current.Permissions.ToolOverrides is { Count: > 0 })
            {
                foreach (var (mode, entry) in settings.Current.Permissions.ToolOverrides)
                {
                    options.ToolOverrides[mode] = entry;
                }
            }
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Failed to load permission settings");
        }
    }

    private static ToolPermissionRule? ParsePermissionRuleValue(string ruleValue)
    {
        if (string.IsNullOrEmpty(ruleValue))
            return null;

        var parenIndex = ruleValue.IndexOf('(');
        if (parenIndex > 0 && ruleValue.EndsWith(')'))
        {
            var toolName = ruleValue[..parenIndex];
            var ruleContent = ruleValue.Substring(parenIndex + 1, ruleValue.Length - parenIndex - 2);
            return new ToolPermissionRule
            {
                ToolName = toolName,
                RuleContent = ruleContent,
                Description = $"From settings.json: {ruleValue}"
            };
        }

        return new ToolPermissionRule
        {
            ToolName = ruleValue,
            Description = $"From settings.json: {ruleValue}"
        };
    }
}
