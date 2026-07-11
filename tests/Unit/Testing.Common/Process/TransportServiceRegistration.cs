using Microsoft.Extensions.DependencyInjection;

namespace Testing.Common.Process;

/// <summary>
/// Transport 层 DI 注册扩展 — 委托到 Transport.Impl 的统一注册方法
/// </summary>
public static class TransportServiceRegistration
{
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

    /// <summary>
    /// 注册全部 Transport 实现，默认使用 Stdio
    /// </summary>
    public static IServiceCollection AddAllTransports(this IServiceCollection services)
    {
        return services.AddTransportServices(TransportMode.Stdio);
    }
}
