namespace JoinCode.Pipe;

/// <summary>
/// 管道服务注册 — 向 DI 容器注册 Bridge 管道相关服务
/// </summary>
public static class PipeServiceRegistration
{
    /// <summary>
    /// 注册管桥服务 — 将 BridgePipeHostedService 注册为托管服务，装配心跳、路由注册器和桥接服务器
    /// </summary>
    /// <param name="services">服务集合</param>
    /// <returns>追加注册后的服务集合</returns>
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
