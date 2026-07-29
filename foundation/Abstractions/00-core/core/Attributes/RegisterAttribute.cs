namespace JoinCode.Abstractions.Attributes;

/// <summary>
/// 标记服务类自动注册到 DI 容器 — 源码生成器据此生成 AddSingleton/AddScoped/AddTransient 注册代码
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class RegisterAttribute : Attribute
{
    /// <summary>
    /// 注册的接口类型，为 null 时仅注册实现类型
    /// </summary>
    public Type? InterfaceType { get; }

    /// <summary>
    /// 服务生命周期，默认 Singleton
    /// </summary>
    public ServiceLifetime Lifetime { get; }

    public RegisterAttribute() => Lifetime = ServiceLifetime.Singleton;

    public RegisterAttribute(ServiceLifetime lifetime) => Lifetime = lifetime;

    public RegisterAttribute(Type interfaceType)
    {
        InterfaceType = interfaceType ?? throw new ArgumentNullException(nameof(interfaceType));
        Lifetime = ServiceLifetime.Singleton;
    }

    public RegisterAttribute(Type interfaceType, ServiceLifetime lifetime)
    {
        InterfaceType = interfaceType ?? throw new ArgumentNullException(nameof(interfaceType));
        Lifetime = lifetime;
    }
}

/// <summary>
/// 服务生命周期 — 与 Microsoft.Extensions.DependencyInjection.ServiceLifetime 对齐
/// </summary>
public enum ServiceLifetime
{
    Singleton = 0,
    Scoped = 1,
    Transient = 2
}
