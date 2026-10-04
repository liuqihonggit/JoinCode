namespace Core.Utils;

/// <summary>
/// Actor 系统 — 顶层 Actor 注册表 + 路径寻址 + Guardian 兜底(DSG033 S4)。
/// <para>对齐 Akka ActorSystem: 所有顶层 Actor 的 Guardian,Dispose 时级联停止全部。</para>
/// <para>轻量实现: 本地注册表 + 路径寻址,不含远程部署/邮件序列化。</para>
/// </summary>
public sealed class ActorSystem : IAsyncDisposable {
    private readonly ConcurrentDictionary<string, IAsyncDisposable> _actors = new(StringComparer.Ordinal);
    private int _disposed;

    /// <summary>系统名称(默认 "user",对齐 Akka /user Guardian)</summary>
    public string Name { get; }

    /// <summary>系统级事件流 — 发布 DeadLetter 等系统事件(Akka 对齐)</summary>
    public EventStream EventStream { get; } = new();

    /// <summary>服务发现 — 按 service key 注册/查找 Actor 引用(Akka Receptionist 对齐)</summary>
    public Receptionist Receptionist { get; } = new();

    /// <summary>构造 Actor 系统</summary>
    /// <param name="name">系统名称(默认 "user")</param>
    public ActorSystem(string name = "user") {
        Name = name;
    }

    /// <summary>
    /// 注册顶层 Actor — 返回传入实例便于链式调用。
    /// <para>重复注册同一 actorId 会覆盖旧引用(旧 Actor 不自动 Dispose,调用方负责)。</para>
    /// </summary>
    public T Register<T>(string actorId, T actor) where T : IAsyncDisposable {
        ThrowIfDisposed();
        _actors[actorId] = actor;
        return actor;
    }

    /// <summary>
    /// 注销顶层 Actor — 从注册表移除,不 Dispose(调用方负责)。
    /// </summary>
    public bool Unregister(string actorId) {
        ThrowIfDisposed();
        return _actors.TryRemove(actorId, out _);
    }

    /// <summary>
    /// 路径寻址 — 按 actorId 返回注册的 Actor 实例。
    /// <para>路径格式: "actorId"(顶层) 或 "parent/child"(未来扩展层级寻址)。</para>
    /// </summary>
    public IAsyncDisposable? Selection(string path) {
        if (_actors.TryGetValue(path, out var actor)) return actor;
        return null;
    }

    /// <summary>
    /// 创建 ActorSelection — 通过路径寻址 Actor,支持 Tell/Identify(Akka 对齐)。
    /// </summary>
    /// <param name="path">Actor 路径(如 "my-actor")</param>
    /// <returns>ActorSelection 实例</returns>
    public ActorSelection ActorSelection(string path) {
        ThrowIfDisposed();
        return new ActorSelection(this, path);
    }

    /// <summary>
    /// 用 Props 创建并注册 Actor — 解耦创建与注册(Akka ActorOf 对齐)。
    /// </summary>
    /// <typeparam name="TActor">Actor 类型</typeparam>
    /// <param name="props">创建配置(封装工厂委托)</param>
    /// <param name="name">注册名称</param>
    /// <returns>创建的 Actor 实例</returns>
    public TActor ActorOf<TActor>(Props<TActor> props, string name) where TActor : IAsyncDisposable {
        ArgumentNullException.ThrowIfNull(props);
        ThrowIfDisposed();
        var actor = props.Create();
        _actors[name] = actor;
        return actor;
    }

    /// <summary>获取所有已注册顶层 Actor 标识快照</summary>
    public string[] GetRegisteredIds() => _actors.Keys.ToArray();

    /// <summary>
    /// 关闭系统 — 级联 Dispose 所有注册的顶层 Actor(Guardian 兜底)。
    /// <para>任何 Actor Dispose 异常忽略,确保全部尝试释放。</para>
    /// </summary>
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var actor in _actors.Values) {
            try { await actor.DisposeAsync().ConfigureAwait(false); }
            catch (Exception ex) { Console.WriteLine($"[ActorSystem:{Name}] Actor Dispose 异常忽略: {ex.Message}"); }
        }
        _actors.Clear();
    }

    private void ThrowIfDisposed() {
        if (Volatile.Read(ref _disposed) != 0)
            throw new ObjectDisposedException(nameof(ActorSystem));
    }
}
