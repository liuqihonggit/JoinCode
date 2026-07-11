namespace JoinCode.Vault.DependencyInjection;

public static partial class ServiceRegistration
{
    public static IServiceCollection AddVaultServices(this IServiceCollection services, Func<IServiceProvider, string>? storagePathFactory = null)
    {
        services.AddVaultStateServices();
        services.AddMemdirServices(storagePathFactory);
        return services;
    }

    public static IServiceCollection AddVaultStateServices(this IServiceCollection services)
    {
        var stateMode = System.Environment.GetEnvironmentVariable(JccEnvVar.StateMode.ToValue());
        if (string.Equals(stateMode, "InMemory", StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IStateService>(sp =>
            {
                if (System.Environment.GetEnvironmentVariable("JCC_DI_TRACE") == "1")
                    System.Console.Error.WriteLine("[DI] + IStateService (InMemory)");
                var svc = new InMemoryStateService(sp.GetRequiredService<IClockService>());
                if (System.Environment.GetEnvironmentVariable("JCC_DI_TRACE") == "1")
                    System.Console.Error.WriteLine("[DI] - IStateService (InMemory)");
                return svc;
            });
        }

        return services;
    }

    public static IServiceCollection AddMemdirServices(this IServiceCollection services, Func<IServiceProvider, string>? storagePathFactory = null)
    {
        if (storagePathFactory is not null)
        {
            services.AddSingleton(sp =>
            {
                var storagePath = storagePathFactory(sp);
                return new MemdirOptions { StoragePath = storagePath };
            });
        }

        return services;
    }
}
