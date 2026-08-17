namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 插件副作用唯一入口 — 对齐 Cordis ctx
/// <para>每个注册方法返回 NonEmptyUndo,框架自动登记到撤销链</para>
/// <para>插件无法绕过 ctx 直接访问 IServiceCollection 做副作用</para>
/// <para>方案C-P1: 基础结构 + RegisterService + Effect;P2 加 RegisterCommand/Hook/Skill/Agent</para>
/// </summary>
public sealed class PluginContext
{
    private readonly string _pluginName;
    private readonly IServiceCollection _services;
    private readonly List<NonEmptyUndo> _undoChain = new();
    private readonly List<IAsyncDisposable> _asyncUndoChain = new();

    /// <summary>创建插件上下文 — 由 WorkflowPluginHost 构造,插件不应直接调用</summary>
    public PluginContext(string pluginName, IServiceCollection services)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);
        ArgumentNullException.ThrowIfNull(services);
        _pluginName = pluginName;
        _services = services;
    }

    /// <summary>插件名</summary>
    public string PluginName => _pluginName;

    /// <summary>注册 DI 服务 — 粗粒度,撤销 = ServiceProvider.Dispose</summary>
    public void RegisterService<TService, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TImpl>(Microsoft.Extensions.DependencyInjection.ServiceLifetime lifetime = Microsoft.Extensions.DependencyInjection.ServiceLifetime.Singleton)
        where TImpl : class, TService
        where TService : class
    {
        _services.Add(new ServiceDescriptor(typeof(TService), typeof(TImpl), lifetime));
    }

    /// <summary>
    /// 自定义副作用 — 对齐 Cordis ctx.effect(disposer)
    /// <para>factory 返回 IDisposable,Dispose 时撤销副作用</para>
    /// <para>撤销函数自动加入撤销链,卸载时逆序执行</para>
    /// </summary>
    public void Effect(Func<IDisposable> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var disposable = factory();
        _undoChain.Add(new NonEmptyUndo(disposable.Dispose));
    }

    /// <summary>
    /// 自定义异步副作用 — 对齐 Cordis ctx.effect(async disposer)
    /// <para>factory 返回 IAsyncDisposable,DisposeAsync 时异步撤销副作用</para>
    /// <para>异步撤销链在卸载时逆序 await 执行,在同步撤销链之前</para>
    /// </summary>
    public void Effect(Func<IAsyncDisposable> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        var disposable = factory();
        _asyncUndoChain.Add(disposable);
    }

    /// <summary>批量配置 DI 服务 — 收敛入口,插件不直接持有 IServiceCollection</summary>
    public void ConfigureServices(Action<IServiceCollection> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(_services);
    }

    /// <summary>获取撤销链(逆序) — PluginManager 卸载时调用</summary>
    internal IReadOnlyList<NonEmptyUndo> GetUndoChain() => _undoChain;

    /// <summary>获取异步撤销链(逆序) — PluginManager 卸载时先于同步撤销链执行</summary>
    public IReadOnlyList<IAsyncDisposable> GetAsyncUndoChain() => _asyncUndoChain;
}
