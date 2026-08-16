namespace JoinCode.Abstractions.Entity;

/// <summary>
/// 插件资源基类 — 每个命令/钩子/技能/Agent 都是一个 Resource
/// <para>继承 Entity 获得 ObjectId + 生命周期状态机 + ObjectIdManager 全局登记</para>
/// <para>实现 IPluginHeartbeat 提供惰性存活检测 — 使用时检测上层心跳,零轮询开销</para>
/// <para>引用计数:其他插件引用此资源时 AddReference,放弃时 ReleaseReference</para>
/// <para>后台扫描:卸载后通过 ObjectIdManager.IsRegistered 验证 ObjectId 已注销</para>
/// </summary>
public abstract class PluginResourceBase : Entity, IPluginHeartbeat
{
    /// <summary>所属插件名</summary>
    public string OwnerPluginName { get; }

    /// <summary>资源类型(Command/Hook/Skill/Agent)</summary>
    public PluginResourceKind Kind { get; }

    /// <summary>引用计数 — 多少其他插件的资源引用了此资源</summary>
    public int ReferenceCount => _refCount;
    private volatile int _refCount;

    /// <summary>引用方插件名集合 — 用于连带卸载时通知引用方</summary>
    private readonly ConcurrentDictionary<string, byte> _consumers = new();

    /// <summary>是否存活 — volatile bool,纳秒级读取</summary>
    public bool IsAlive => _isAlive;
    private volatile bool _isAlive = true;

    /// <summary>最后心跳时刻</summary>
    public DateTime LastHeartbeatAt { get; private set; }

    /// <summary>死亡事件 — 心跳停止时触发,下层据此通知死亡</summary>
    public event EventHandler? OnDeath;

    /// <summary>创建插件资源</summary>
    protected PluginResourceBase(string ownerPluginName, PluginResourceKind kind, string displayName)
        : base(ObjectType.Resource, displayName: displayName, registerToSessionRouter: false)
    {
        OwnerPluginName = ownerPluginName ?? throw new ArgumentNullException(nameof(ownerPluginName));
        Kind = kind;
        LastHeartbeatAt = CreatedAt;
    }

    /// <summary>
    /// 增加引用 — 返回引用句柄,using/Dispose 时自动减少引用
    /// <para>插件B 引用 插件A 的资源时调用,引用计数 +1</para>
    /// </summary>
    public ResourceReferenceHandle AddReference(string consumerPluginName)
    {
        ArgumentNullException.ThrowIfNull(consumerPluginName);
        _consumers.TryAdd(consumerPluginName, 0);
        Interlocked.Increment(ref _refCount);
        return new ResourceReferenceHandle(() => ReleaseReference(consumerPluginName));
    }

    /// <summary>
    /// 减少引用 — 引用方放弃此资源时调用
    /// <para>引用计数 -1,归零时表示资源可安全卸载</para>
    /// </summary>
    public void ReleaseReference(string consumerPluginName)
    {
        ArgumentNullException.ThrowIfNull(consumerPluginName);
        _consumers.TryRemove(consumerPluginName, out _);
        Interlocked.Decrement(ref _refCount);
    }

    /// <summary>
    /// 心跳检测 — 使用此资源前调用,如果提供者已死亡则抛 PluginDeadException
    /// <para>惰性检测:每次跨插件调用时检测,读 volatile bool 纳秒级</para>
    /// </summary>
    public void EnsureAlive()
    {
        if (!IsAlive)
            throw new PluginDeadException(DisplayName, OwnerPluginName);
    }

    /// <summary>
    /// 获取所有引用方插件名 — 用于连带卸载时通知引用方放弃引用
    /// </summary>
    public IReadOnlyCollection<string> GetConsumers() => _consumers.Keys.ToList();

    /// <summary>
    /// 刷新心跳 — 插件每次活动时调用,同时更新 LastActivityAt 和 LastHeartbeatAt
    /// </summary>
    public new void Touch()
    {
        LastActivityAt = DateTime.UtcNow;
        LastHeartbeatAt = LastActivityAt;
    }

    /// <summary>
    /// 标记死亡 — 不可逆,触发 OnDeath 事件
    /// <para>心跳停止 → 本层死亡 → 下层通过 OnDeath 通知死亡</para>
    /// </summary>
    public void MarkDead()
    {
        if (!_isAlive) return;
        _isAlive = false;
        OnDeath?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// 子类覆写:资源特定清理逻辑(从 Registry 移除等)
    /// <para>框架已处理 ObjectIdManager.Unregister,子类只需做 Registry 级清理</para>
    /// </summary>
    protected virtual void OnResourceDispose() { }

    /// <summary>
    /// Entity.OnDispose 实现 — 标记死亡 + 子类清理
    /// </summary>
    protected sealed override void OnDispose()
    {
        MarkDead();
        OnResourceDispose();
    }
}
