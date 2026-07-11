namespace JoinCode.Sdk;

public static class JoinCodeServiceCollectionExtensions
{
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
            Provider = options.Provider.ToValue(),
            ModelId = options.ModelId,
            ApiKey = options.ApiKey ?? string.Empty,
            Endpoint = options.BaseUrl,
        };

        services.AddSingleton(providerConfig);

        Api.ApiRegistration.AddSKKernelAdapter(services, providerConfig);

        Infrastructure.Localization.LocalizerInitializer.Initialize(options.Language);
    }
}
