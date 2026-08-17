namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 工作流插件基类 — 所有插件必须继承此类
/// <para>继承 Entity 获得 ObjectId + 生命周期状态机 + ObjectIdManager 全局登记</para>
/// <para>实现 IWorkflowPlugin 提供插件三段式生命周期(Load → InitializeAsync → Unload)</para>
/// <para>实现 IPluginHeartbeat 提供惰性存活检测 — 使用上层资源时检测心跳</para>
/// <para>持有资源登记表:每个命令/钩子/技能/Agent 都是一个 PluginResourceBase</para>
/// <para>持有 UI 资源表:可逆操作时刷新界面(图标重排等)</para>
/// <para>持有非托管资源表:SafeHandle 包装的非托管内存,卸载时逐个释放</para>
/// </summary>
public abstract class WorkflowPluginBase : Entity, IWorkflowPlugin, IPluginHeartbeat
{
    private readonly Dictionary<ObjectId, PluginResourceBase> _resources = new();
    private readonly object _resourceLock = new();
    private volatile bool _isAlive = true;
    private DateTime _lastHeartbeatAt;

    /// <summary>创建工作流插件基类</summary>
    protected WorkflowPluginBase(string displayName) : base(ObjectType.Plugin, displayName: displayName, registerToSessionRouter: false)
    {
        _lastHeartbeatAt = CreatedAt;
    }

    /// <summary>插件名称 — 子类实现</summary>
    public abstract string Name { get; }

    /// <summary>插件版本 — 子类实现</summary>
    public abstract string Version { get; }

    /// <summary>插件描述 — 子类实现</summary>
    public abstract string Description { get; }

    /// <summary>加载插件 — 副作用唯一入口 PluginContext,对齐 Cordis ctx</summary>
    /// <para>子类覆写,通过 ctx.RegisterService/ConfigureServices/Effect 注册副作用</para>
    public abstract Task<OperationResult> LoadAsync(PluginContext ctx, CancellationToken cancellationToken = default);

    /// <summary>初始化插件 - 获取服务依赖 — 子类实现</summary>
    public abstract Task<OperationResult> InitializeAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default);

    /// <summary>所有已登记的资源</summary>
    public IReadOnlyCollection<PluginResourceBase> Resources
    {
        get
        {
            lock (_resourceLock)
            {
                return _resources.Values.ToList();
            }
        }
    }

    /// <summary>UI 资源表 — 插件持有的界面资源</summary>
    public UiResourceTable UiResources { get; } = new();

    /// <summary>非托管资源表 — SafeHandle 包装的非托管内存</summary>
    public UnmanagedResourceTable UnmanagedResources { get; } = new();

    /// <summary>是否存活 — volatile bool,纳秒级读取</summary>
    public bool IsAlive => _isAlive;

    /// <summary>最后心跳时刻</summary>
    public DateTime LastHeartbeatAt => _lastHeartbeatAt;

    /// <summary>死亡事件 — 心跳停止时触发</summary>
    public event EventHandler? OnDeath;

    /// <summary>
    /// 登记资源 — 子类注册命令/钩子/技能/Agent 时调用
    /// <para>资源自动获得 ObjectId,后台扫描可通过 ObjectIdManager 验证卸载</para>
    /// </summary>
    protected T RegisterResource<T>(T resource) where T : PluginResourceBase
    {
        ArgumentNullException.ThrowIfNull(resource);
        lock (_resourceLock)
        {
            _resources[resource.ObjectId] = resource;
        }
        return resource;
    }

    /// <summary>
    /// 卸载插件 — 框架统一处理资源释放
    /// <para>1. 标记死亡(心跳停止)</para>
    /// <para>2. 释放所有资源(Entity.Dispose → ObjectIdManager.Unregister)</para>
    /// <para>3. 释放非托管资源(ReleaseAll)</para>
    /// <para>4. 子类清理(OnUnload)</para>
    /// <para>UI 资源事件由 PluginManager 负责广播</para>
    /// </summary>
    public PluginUnloadResult Unload()
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            MarkDead();

            List<PluginResourceBase> snapshot;
            lock (_resourceLock)
            {
                snapshot = _resources.Values.ToList();
                _resources.Clear();
            }
            foreach (var resource in snapshot)
            {
                resource.Dispose();
            }

            UnmanagedResources.ReleaseAll();

            OnUnload();
            return PluginUnloadResult.Success(Name, sw.Elapsed);
        }
        catch (Exception ex)
        {
            return PluginUnloadResult.Failure(ex.Message);
        }
    }

    /// <summary>子类覆写:插件特定清理逻辑 — 在资源释放后调用</summary>
    protected virtual void OnUnload() { }

    /// <summary>
    /// 校验卸载契约 — 加载后由 PluginManager 调用,失败则拒绝加载
    /// <para>对齐方案B:不写对应卸载就不允许加载</para>
    /// <para>默认实现返回 Valid(信任子类已通过 RegisterResource 登记所有副作用)</para>
    /// <para>子类可覆写做更严校验(如校验资源数量与声明副作用一致、disposer 非空等)</para>
    /// </summary>
    public virtual PluginUnloadContract ValidateUnloadContract() => PluginUnloadContract.Valid;

    /// <summary>刷新心跳 — 插件每次活动时调用</summary>
    public new void Touch()
    {
        LastActivityAt = DateTime.UtcNow;
        _lastHeartbeatAt = LastActivityAt;
    }

    /// <summary>标记死亡 — 不可逆,触发 OnDeath 事件</summary>
    public void MarkDead()
    {
        if (!_isAlive) return;
        _isAlive = false;
        OnDeath?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>心跳检测 — 如果已死亡则抛 PluginDeadException</summary>
    public void EnsureAlive()
    {
        if (!_isAlive)
            throw new PluginDeadException(DisplayName, Name);
    }

    /// <summary>Entity.OnDispose 实现 — 确保资源释放</summary>
    protected override void OnDispose()
    {
        MarkDead();
        UnmanagedResources.ReleaseAll();
    }
}
