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
        services.AddEnvSwitch<IStateService>(
            JccEnvVar.StateMode, "InMemory",
            sp => new InMemoryStateService(sp.GetRequiredService<IClockService>()));

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
