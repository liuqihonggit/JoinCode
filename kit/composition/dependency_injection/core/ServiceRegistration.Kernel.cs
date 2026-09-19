
namespace Core.DependencyInjection;

public static partial class ServiceRegistration {
    /// <summary>
    /// 注册 Kernel 及其插件（静态插件模式）。
    /// <para>委托给 <c>JoinCode.Llm.DependencyInjection.ServiceRegistration.AddKernelWithPlugins</c>。</para>
    /// </summary>
    /// <param name="services">DI 容器。</param>
    /// <param name="config">工作流配置（提供 Provider 和 PipeEndpoint）。</param>
    /// <returns>已注册服务的 <see cref="IServiceCollection"/> 实例。</returns>
    public static IServiceCollection AddKernelWithPlugins(
        this IServiceCollection services,
        WorkflowConfig config) {
        JoinCode.Llm.DependencyInjection.ServiceRegistration.AddKernelWithPlugins(services, config.Provider, config.PipeEndpoint);

        // PluginManager — auto-registered via [Register] (both IPluginManager and self-type)

        return services;
    }

    /// <summary>
    /// 注册 Kernel 及其动态插件（运行时加载插件模式）。
    /// <para>委托给 <c>JoinCode.Llm.DependencyInjection.ServiceRegistration.AddKernelWithDynamicPlugins</c>。</para>
    /// </summary>
    /// <param name="services">DI 容器。</param>
    /// <param name="config">工作流配置（提供 Provider）。</param>
    /// <returns>已注册服务的 <see cref="IServiceCollection"/> 实例。</returns>
    public static IServiceCollection AddKernelWithDynamicPlugins(
        this IServiceCollection services,
        WorkflowConfig config) {
        JoinCode.Llm.DependencyInjection.ServiceRegistration.AddKernelWithDynamicPlugins(services, config.Provider);

        // PluginManager — auto-registered via [Register] (both IPluginManager and self-type)

        return services;
    }
}