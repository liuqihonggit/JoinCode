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
    private readonly CancellationToken _shutdown;
    private readonly List<NonEmptyUndo> _undoChain = new();
    private readonly List<IAsyncDisposable> _asyncUndoChain = new();

    /// <summary>
    /// 创建插件上下文 — 由 WorkflowPluginHost 构造,插件不应直接调用
    /// <para>shutdown 绑定 PluginManager 的卸载令牌,RunBackgroundTask 自动绑定此令牌</para>
    /// </summary>
    public PluginContext(string pluginName, IServiceCollection services, CancellationToken shutdown = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pluginName);
        ArgumentNullException.ThrowIfNull(services);
        _pluginName = pluginName;
        _services = services;
        _shutdown = shutdown;
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

    /// <summary>
    /// 提交后台任务。自动绑定 Shutdown,卸载时自动取消并等待(带超时)。
    /// <para>对齐 Cordis ctx.RunBackgroundTask(ct => ...)</para>
    /// <para>work 务必协作式响应 CancellationToken,否则卸载时等待超时抛 TimeoutException</para>
    /// <para>waitOnUnload: null=默认2秒,Zero=不等待(立即返回)</para>
    /// </summary>
    public Task RunBackgroundTask(Func<CancellationToken, Task> work, TimeSpan? waitOnUnload = null)
    {
        ArgumentNullException.ThrowIfNull(work);

        var token = _shutdown;
        var pluginName = _pluginName;
        var task = Task.Run(async () =>
        {
            try { await work(token).ConfigureAwait(false); }
            catch (OperationCanceledException) { }
            catch (Exception ex) { Console.WriteLine($"[{pluginName}] 后台任务异常: {ex.Message}"); }
        }, token);

        var wait = waitOnUnload ?? TimeSpan.FromSeconds(2);
        _undoChain.Add(new NonEmptyUndo(() => WaitForBackgroundTaskExit(task, wait, pluginName)));
        return task;
    }

    /// <summary>
    /// 提交后台任务(同步重载)。自动绑定 Shutdown,卸载时自动取消并等待(带超时)。
    /// </summary>
    public Task RunBackgroundTask(Action<CancellationToken> work, TimeSpan? waitOnUnload = null)
        => RunBackgroundTask(ct => { work(ct); return Task.CompletedTask; }, waitOnUnload);

    private static void WaitForBackgroundTaskExit(Task task, TimeSpan wait, string pluginName)
    {
        if (wait <= TimeSpan.Zero) return;
        try
        {
            if (!task.Wait(wait))
                throw new TimeoutException(
                    $"后台任务在 {wait.TotalSeconds:0.##}s 内未退出。" +
                    $"请确保任务正确响应 CancellationToken。");
        }
        catch (AggregateException aex) when (aex.InnerExceptions.All(e => e is OperationCanceledException))
        {
            Console.WriteLine($"[{pluginName}] 后台任务已正常取消");
        }
    }

    /// <summary>获取撤销链(逆序) — PluginManager 卸载时调用</summary>
    public IReadOnlyList<NonEmptyUndo> GetUndoChain() => _undoChain;

    /// <summary>获取异步撤销链(逆序) — PluginManager 卸载时先于同步撤销链执行</summary>
    public IReadOnlyList<IAsyncDisposable> GetAsyncUndoChain() => _asyncUndoChain;
}
