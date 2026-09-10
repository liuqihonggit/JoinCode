namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 未声明服务访问异常 — 对齐 DSH capability-based Proxy 拒绝
/// <para>带修复提示：告知需在 inject 数组中添加该服务</para>
/// <para>不用 DispatchProxy（AOT 不兼容），改运行时 ServiceLookup 校验</para>
/// </summary>
public sealed class ServiceNotDeclaredException : Exception
{
    /// <summary>被拒绝的服务名</summary>
    public string ServiceName { get; }

    /// <summary>已声明的 inject 列表</summary>
    public string[] DeclaredInjects { get; }

    /// <param name="serviceName">被拒绝的服务名</param>
    /// <param name="declaredInjects">已声明的 inject 列表</param>
    public ServiceNotDeclaredException(string serviceName, string[] declaredInjects)
        : base($"[INF-CAPABILITY] 服务 '{serviceName}' 未在 inject 中声明。已声明: [{string.Join(", ", declaredInjects)}]。请在 inject 数组中添加 '{serviceName}'。")
    {
        ServiceName = serviceName;
        DeclaredInjects = declaredInjects;
    }
}

/// <summary>
/// 注入声明特性 — 标记需要注入的服务
/// <para>源码生成器扫描此特性，编译时校验 inject 声明完整性</para>
/// <para>对齐 DSH @Inject 装饰器</para>
/// </summary>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Parameter)]
public sealed class InjectAttribute : Attribute
{
    /// <summary>声明的服务名列表</summary>
    public string[] Services { get; }

    /// <param name="services">声明的服务名</param>
    public InjectAttribute(params string[] services) => Services = services;
}

/// <summary>
/// capability 门禁 — 运行时校验已声明 inject 才放行
/// <para>对齐 DSH capability-based Proxy：未声明访问被拒绝</para>
/// <para>不用 DispatchProxy（AOT 不兼容），改 FrozenSet + 运行时校验</para>
/// <para>配合源码生成器编译时检查（后续 #9 扩展）</para>
/// </summary>
public sealed class CapabilityGate
{
    private readonly FrozenSet<string> _declaredInjects;

    /// <param name="declaredInjects">已声明的 inject 服务名</param>
    public CapabilityGate(IEnumerable<string> declaredInjects)
    {
        ArgumentNullException.ThrowIfNull(declaredInjects);
        _declaredInjects = declaredInjects.ToFrozenSet();
    }

    /// <summary>
    /// 校验服务已声明 — 未声明抛 ServiceNotDeclaredException（带修复提示）
    /// </summary>
    public T RequireService<T>(string serviceName, Func<T> resolve)
    {
        if (!_declaredInjects.Contains(serviceName))
        {
            throw new ServiceNotDeclaredException(serviceName, _declaredInjects.ToArray());
        }
        return resolve();
    }

    /// <summary>检查服务是否已声明</summary>
    public bool IsDeclared(string serviceName) => _declaredInjects.Contains(serviceName);

    /// <summary>已声明的服务集合</summary>
    public IReadOnlySet<string> DeclaredServices => _declaredInjects;
}
