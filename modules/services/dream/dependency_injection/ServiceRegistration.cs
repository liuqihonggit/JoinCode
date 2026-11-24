namespace JoinCode.Dream.DependencyInjection;

/// <summary>
/// Dream DI 注册
/// </summary>
public static partial class ServiceRegistration {
    /// <summary>
    /// 添加做梦系统服务（内存存储）
    /// </summary>
    public static IServiceCollection AddDreamServices(
        this IServiceCollection services,
        Func<AutoDreamConfig, AutoDreamConfig>? configureOptions = null) {
        var config = new AutoDreamConfig();
        config = configureOptions?.Invoke(config) ?? config;
        services.AddSingleton(config);

        return services;
    }

    /// <summary>
    /// 添加做梦系统服务（持久化存储）
    /// </summary>
    public static IServiceCollection AddDreamServicesWithPersistence(
        this IServiceCollection services,
        Func<AutoDreamConfig, AutoDreamConfig>? configureOptions = null) {
        var config = new AutoDreamConfig();
        config = configureOptions?.Invoke(config) ?? config;
        services.AddSingleton(config);

        return services;
    }

    /// <summary>
    /// 初始化做梦系统
    /// </summary>
    public static IServiceProvider InitializeDreamSystem(this IServiceProvider serviceProvider) {
        return serviceProvider;
    }

    /// <summary>
    /// 初始化做梦系统（持久化存储）
    /// </summary>
    public static async Task<IServiceProvider> InitializeDreamSystemWithPersistenceAsync(
        this IServiceProvider serviceProvider) {
        if (serviceProvider.GetService<IDreamTaskRegistry>() is PersistentDreamTaskRegistry registry) {
            await registry.LoadActiveTasksAsync().ConfigureAwait(false);
        }

        return serviceProvider;
    }

    /// <summary>
    /// 添加 Dream 插件服务
    /// <para>B类配置桥 — WorkflowConfig（settings.current.autoMemoryEnabled/autoDreamEnabled 已映射，
    /// 默认 true 对齐 TS）流入 AutoDreamConfig；未注册 WorkflowConfig 时保持插件档默认。</para>
    /// </summary>
    public static IServiceCollection AddDreamPluginServices(this IServiceCollection services) {
        services.AddSingleton<AutoDreamConfig>(sp => {
            var config = AutoDreamConfigBuilder.Create()
                .WithMinSessions(2)
                .Build();

            var workflow = sp.GetService<WorkflowConfig>();
            if (workflow is not null) {
                config = config with {
                    AutoMemoryEnabled = workflow.AutoMemoryEnabled,
                    Enabled = workflow.AutoDreamEnabled,
                };
            }

            return config;
        });

        // TASK136 C1 真机探针：插件容器是**独立** ServiceProvider，主容器的 [Register] 服务看不见。
        // 此前这里只注册 AutoDreamConfig，IDreamFeature / ISessionScanner 全缺 → 自动整合钩子永久等待。
        // 直接调用生成的 AddJoinCodeDreamAutoRegisteredServices 会拖入引用程序集（ChatClient 依赖主容器
        // IQueryService），因此只注册 Dream 自身运行时需要的服务。
        services.AddSingleton<IFileSystem>(_ => new IO.FileSystem.PhysicalFileSystem());
        services.AddSingleton<IChatCompletionClient>(sp => new ChatCompletionClient(
            sp.GetRequiredService<IChatClient>()));
        services.AddSingleton<ISessionScanner>(sp => new DefaultSessionScanner(
            sp.GetRequiredService<AutoDreamConfig>(),
            sp.GetRequiredService<IFileSystem>(),
            sp.GetService<ILogger<DefaultSessionScanner>>()));
        services.AddSingleton<IDreamTaskPersistence>(sp => new JsonFileDreamTaskPersistence(
            sp.GetRequiredService<AutoDreamConfig>(),
            sp.GetRequiredService<IFileOperationService>(),
            sp.GetService<ILogger<JsonFileDreamTaskPersistence>>()));
        services.AddSingleton<IDreamTaskRegistry, PersistentDreamTaskRegistry>();
        services.AddSingleton<ConsolidationLock>(sp => new ConsolidationLock(
            sp.GetRequiredService<AutoDreamConfig>(),
            sp.GetRequiredService<IFileSystem>()));
        services.AddSingleton<IDreamFeature, DreamFeature>();

        return services;
    }
}