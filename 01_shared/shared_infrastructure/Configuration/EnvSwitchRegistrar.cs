namespace Infrastructure.Configuration;

public static class EnvSwitchRegistrar
{
    public static IServiceCollection AddEnvSwitch<TService>(
        this IServiceCollection services,
        JccEnvVar envVar,
        string altMode,
        Func<IServiceProvider, TService> altFactory,
        Func<IServiceProvider, TService> defaultFactory)
        where TService : class
    {
        var mode = EnvHelper.Get(envVar);
        var serviceName = typeof(TService).Name;
        if (string.Equals(mode, altMode, StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<TService>(sp => TraceFactory(altFactory, serviceName, altMode, sp));
        }
        else
        {
            services.AddSingleton<TService>(sp => TraceFactory(defaultFactory, serviceName, "Default", sp));
        }

        return services;
    }

    public static IServiceCollection AddEnvSwitch<TService>(
        this IServiceCollection services,
        JccEnvVar envVar,
        string altMode,
        Func<IServiceProvider, TService> altFactory)
        where TService : class
    {
        var mode = EnvHelper.Get(envVar);
        var serviceName = typeof(TService).Name;
        if (string.Equals(mode, altMode, StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<TService>(sp => TraceFactory(altFactory, serviceName, altMode, sp));
        }

        return services;
    }

    public static TService TraceFactory<TService>(
        Func<IServiceProvider, TService> factory,
        string serviceName,
        string mode,
        IServiceProvider sp) where TService : class
    {
        JoinCode.Abstractions.Utils.Diagnostics.Diag.WriteDiTrace($"[DI] + {serviceName} ({mode})");
        var svc = factory(sp);
        JoinCode.Abstractions.Utils.Diagnostics.Diag.WriteDiTrace($"[DI] - {serviceName} ({mode})");
        return svc;
    }
}
