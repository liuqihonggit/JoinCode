namespace JoinCode.Sdk;

/// <summary>
/// JoinCode SDK 服务注册扩展 — 提供 AddJoinCode 扩展方法将 JoinCode 服务注入外部宿主的 DI 容器。
/// </summary>
public static class JoinCodeServiceCollectionExtensions
{
    /// <summary>
    /// 将 JoinCode 服务添加到 IHostBuilder 的 DI 容器。
    /// </summary>
    /// <param name="hostBuilder">宿主构建器。</param>
    /// <param name="configure">JoinCode 选项配置委托。</param>
    /// <returns>宿主构建器（链式调用）。</returns>
    public static IHostBuilder AddJoinCode(this IHostBuilder hostBuilder, Action<JoinCodeOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        hostBuilder.ConfigureServices((context, services) =>
        {
            var options = new JoinCodeOptions();
            configure(options);

            services.AddJoinCodeCore(options);
        });

        return hostBuilder;
    }

    /// <summary>
    /// 将 JoinCode 服务添加到 IServiceCollection。
    /// </summary>
    /// <param name="services">服务集合。</param>
    /// <param name="configure">JoinCode 选项配置委托。</param>
    /// <returns>服务集合（链式调用）。</returns>
    public static IServiceCollection AddJoinCode(this IServiceCollection services, Action<JoinCodeOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new JoinCodeOptions();
        configure(options);

        services.AddJoinCodeCore(options);

        return services;
    }

    private static void AddJoinCodeCore(this IServiceCollection services, JoinCodeOptions options)
    {
        var providerConfig = new ProviderConfig
        {
            Vendor = options.Vendor.ToValue(),
            ModelId = options.ModelId,
            ApiKey = options.ApiKey ?? string.Empty,
            Endpoint = options.BaseUrl,
        };

        services.AddSingleton(providerConfig);

        JoinCode.Llm.DependencyInjection.ServiceRegistration.AddLlmServices(services, providerConfig);

        Infrastructure.Localization.LocalizerInitializer.Initialize(options.Language);
    }
}
