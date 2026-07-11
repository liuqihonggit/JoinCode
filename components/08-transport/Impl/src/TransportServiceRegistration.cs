namespace JoinCode.Transport;

/// <summary>
/// Transport 层 DI 注册扩展 — 一键切换 Stdio/SSE 传输模式
/// </summary>
/// <example>
/// <code>
/// // 一行切换传输模式
/// services.AddTransportMode(TransportMode.Stdio);
/// services.AddTransportMode(TransportMode.Sse);
/// </code>
/// </example>
public static class TransportServiceRegistration
{
    /// <summary>
    /// 根据传输模式注册 IAgentTransport 实现
    /// </summary>
    public static IServiceCollection AddTransportMode(this IServiceCollection services, TransportMode mode)
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
        return services.AddTransportMode(TransportMode.Stdio);
    }

    /// <summary>
    /// 注册 SSE 传输实现
    /// </summary>
    public static IServiceCollection AddSseTransport(this IServiceCollection services)
    {
        return services.AddTransportMode(TransportMode.Sse);
    }
}
