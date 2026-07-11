namespace JoinCode.Guard.DependencyInjection;

public static partial class ServiceRegistration
{
    public static IServiceCollection AddGuardServices(this IServiceCollection services)
    {
        services.AddPermissionServices();
        services.AddPermissionHookServices();
        services.AddSecurityServices();
        services.AddConfigurationServices();
        services.AddHookSystem();
        return services;
    }

    public static IServiceCollection AddSecurityServices(this IServiceCollection services)
    {
        return services;
    }

    public static IServiceCollection AddConfigurationServices(this IServiceCollection services)
    {
        return services;
    }

    public static IServiceCollection AddPermissionServices(this IServiceCollection services)
    {
        services.AddOptions<PermissionConfig>()
            .Configure<IFileSystem>((options, fs) => {
                var defaultConfig = PermissionConfig.CreateDefault();
                options.AutoApprovedTools = defaultConfig.AutoApprovedTools;
                options.AutoRejectedTools = defaultConfig.AutoRejectedTools;
                options.DangerousOperationPatterns = defaultConfig.DangerousOperationPatterns;
                options.WriteOperationPatterns = defaultConfig.WriteOperationPatterns;
                options.ReadOperationPatterns = defaultConfig.ReadOperationPatterns;
                options.ShellOperationPatterns = defaultConfig.ShellOperationPatterns;
                options.SensitivePathPatterns = defaultConfig.SensitivePathPatterns;
                options.DangerousCommandPatterns = defaultConfig.DangerousCommandPatterns;

                LoadPermissionsFromSettings(options, fs);
            });

        return services;
    }

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

            var appDataRoot = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
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

    public static IServiceCollection AddPermissionHookServices(this IServiceCollection services)
    {
        return services;
    }

    private static void LoadPermissionsFromSettings(PermissionConfig options, IFileSystem fs)
    {
        try
        {
            var settings = SettingsLoader.LoadUserSettings(fs);
            if (settings?.Permissions is null)
                return;

            if (settings.Permissions.Allow is { Count: > 0 })
            {
                foreach (var rule in settings.Permissions.Allow)
                {
                    var parsed = ParsePermissionRuleValue(rule);
                    if (parsed is not null && !options.AutoApprovedTools.Any(r =>
                        string.Equals(r.ToolName, parsed.ToolName, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(r.RuleContent ?? "", parsed.RuleContent ?? "", StringComparison.OrdinalIgnoreCase)))
                    {
                        options.AutoApprovedTools.Add(parsed);
                    }
                }
            }

            if (settings.Permissions.Deny is { Count: > 0 })
            {
                foreach (var rule in settings.Permissions.Deny)
                {
                    var parsed = ParsePermissionRuleValue(rule);
                    if (parsed is not null)
                        options.AutoRejectedTools.Add(parsed);
                }
            }

            if (settings.Permissions.Ask is { Count: > 0 })
            {
                foreach (var rule in settings.Permissions.Ask)
                {
                    var parsed = ParsePermissionRuleValue(rule);
                    if (parsed is not null)
                        options.AskRules.Add(parsed);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Trace.WriteLine($"Failed to load permission settings: {ex.Message}");
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
