namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 服务查询结果(ADR 0098)
/// </summary>
public enum ServiceLookup
{
    /// <summary>找到服务</summary>
    Found,
    /// <summary>服务从未注册</summary>
    NotRegistered,
    /// <summary>服务曾注册但提供者已死亡</summary>
    ProviderDead,
}

/// <summary>
/// 服务不可用异常 — 查询服务失败时抛出(ADR 0098)
/// <para>区分 NotRegistered(从未注册)和 ProviderDead(提供者已死亡)</para>
/// </summary>
public sealed class ServiceUnavailableException : Exception
{
    /// <summary>查询的服务类型</summary>
    public Type ServiceType { get; }

    /// <summary>不可用原因</summary>
    public ServiceLookup Reason { get; }

    /// <summary>创建服务不可用异常</summary>
    public ServiceUnavailableException(Type serviceType, ServiceLookup reason)
        : base($"服务 {serviceType.Name} 不可用: {reason}")
    {
        ServiceType = serviceType;
        Reason = reason;
    }
}
