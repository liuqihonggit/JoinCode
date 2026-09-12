namespace JoinCode.Transport.DependencyInjection;

/// <summary>
/// Transport 层 DI 注册
/// </summary>
public static partial class ServiceRegistration
{
    /// <summary>
    /// 根据传输模式注册 IAgentTransport 实现
    /// </summary>
    public static IServiceCollection AddTransportServices(this IServiceCollection services, TransportMode mode)
    {
        return mode switch
        {
            TransportMode.Stdio => services.AddSingleton<IAgentTransport, StdioAgentTransport>(),
            TransportMode.Sse => services.AddSingleton<IAgentTransport, SseAgentTransport>(),
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, $"Unsupported transport mode: {mode}")
        };
    }

    /// <summary>
    /// 注册 Stdio 传输实现
    /// </summary>
    public static IServiceCollection AddStdioTransport(this IServiceCollection services)
    {
        return services.AddTransportServices(TransportMode.Stdio);
    }

    /// <summary>
    /// 注册 SSE 传输实现
    /// </summary>
    public static IServiceCollection AddSseTransport(this IServiceCollection services)
    {
        return services.AddTransportServices(TransportMode.Sse);
    }
}
