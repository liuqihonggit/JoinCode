namespace JoinCode.Pipe;

public static class PipeServiceRegistration
{
    public static IServiceCollection RegisterPipeServices(this IServiceCollection services)
    {
        // HostedService 不适合 [Register]，保留手动注册
        services.AddHostedService<BridgePipeHostedService>(sp =>
        {
            var heartbeatService = sp.GetRequiredService<BridgeHeartbeatService>();
            var routeRegistrar = sp.GetService<IPipeRouteRegistrar>();
            var bridgeServer = sp.GetService<Core.Bridge.BridgeServer>();
            var logger = sp.GetService<ILogger<BridgePipeHostedService>>();
            return new BridgePipeHostedService(heartbeatService, routeRegistrar, bridgeServer, logger);
        });

        return services;
    }
}
