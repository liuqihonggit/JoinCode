namespace Infrastructure.Configuration;

/// <summary>
/// 环境变量切换注册器 — 根据环境变量值在 DI 容器中注册不同实现，并写入 DI Trace 日志
/// </summary>
public static class EnvSwitchRegistrar {
    /// <summary>
    /// 根据环境变量值注册 TService 的不同实现（含默认工厂）
    /// </summary>
    /// <typeparam name="TService">服务类型</typeparam>
    /// <param name="services">DI 服务集合</param>
    /// <param name="envVar">环境变量名</param>
    /// <param name="altMode">替代模式标识值</param>
    /// <param name="altFactory">替代模式工厂</param>
    /// <param name="defaultFactory">默认模式工厂</param>
    /// <returns>DI 服务集合（链式调用）</returns>
    public static IServiceCollection AddEnvSwitch<TService>(
        this IServiceCollection services,
        JccEnvVar envVar,
        string altMode,
        Func<IServiceProvider, TService> altFactory,
        Func<IServiceProvider, TService> defaultFactory)
        where TService : class {
        var mode = EnvHelper.Get(envVar);
        var serviceName = typeof(TService).Name;
        if (string.Equals(mode, altMode, StringComparison.OrdinalIgnoreCase)) {
            services.AddSingleton<TService>(sp => TraceFactory(altFactory, serviceName, altMode, sp));
        } else {
            services.AddSingleton<TService>(sp => TraceFactory(defaultFactory, serviceName, "Default", sp));
        }

        return services;
    }

    /// <summary>
    /// 根据环境变量值注册 TService 的替代实现；环境变量不匹配时不注册
    /// </summary>
    /// <typeparam name="TService">服务类型</typeparam>
    /// <param name="services">DI 服务集合</param>
    /// <param name="envVar">环境变量名</param>
    /// <param name="altMode">替代模式标识值</param>
    /// <param name="altFactory">替代模式工厂</param>
    /// <returns>DI 服务集合（链式调用）</returns>
    public static IServiceCollection AddEnvSwitch<TService>(
        this IServiceCollection services,
        JccEnvVar envVar,
        string altMode,
        Func<IServiceProvider, TService> altFactory)
        where TService : class {
        var mode = EnvHelper.Get(envVar);
        var serviceName = typeof(TService).Name;
        if (string.Equals(mode, altMode, StringComparison.OrdinalIgnoreCase)) {
            services.AddSingleton<TService>(sp => TraceFactory(altFactory, serviceName, altMode, sp));
        }

        return services;
    }

    /// <summary>
    /// 包装工厂调用，前后写入 DI Trace 日志，便于诊断 DI 构建过程
    /// </summary>
    /// <typeparam name="TService">服务类型</typeparam>
    /// <param name="factory">原始工厂</param>
    /// <param name="serviceName">服务名称（用于日志）</param>
    /// <param name="mode">模式标识（用于日志）</param>
    /// <param name="sp">DI 服务提供者</param>
    /// <returns>工厂创建的服务实例</returns>
    public static TService TraceFactory<TService>(
        Func<IServiceProvider, TService> factory,
        string serviceName,
        string mode,
        IServiceProvider sp) where TService : class {
        JoinCode.Abstractions.Utils.Diagnostics.Diag.WriteDiTrace($"[DI] + {serviceName} ({mode})");
        var svc = factory(sp);
        JoinCode.Abstractions.Utils.Diagnostics.Diag.WriteDiTrace($"[DI] - {serviceName} ({mode})");
        return svc;
    }
}