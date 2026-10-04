namespace Core.Utils;

/// <summary>
/// 服务发现 — 注册/查找 Actor 服务引用(Akka Receptionist 对齐)。
/// <para>与 ActorSystem 注册表区别:Receptionist 按 service key 注册(非 ActorId),用于服务发现模式。</para>
/// <para>同一 service key 可注册多个 Actor(负载5制),Find 返回第一个注册的。</para>
/// </summary>
public sealed class Receptionist {
    private readonly ConcurrentDictionary<string, IAsyncDisposable> _services = new(StringComparer.Ordinal);

    /// <summary>
    /// 注册服务 — 按 key 关联 Actor 引用,重复注册覆盖旧引用。
    /// </summary>
    /// <typeparam name="T">Actor 类型</typeparam>
    /// <param name="key">服务标识</param>
    /// <param name="actor">Actor 引用</param>
    /// <returns>传入的 Actor 引用(链式调用)</returns>
    public T Register<T>(string key, T actor) where T : IAsyncDisposable {
        _services[key] = actor;
        return actor;
    }

    /// <summary>
    /// 查找服务 — 按 key 返回关联的 Actor 引用,null 表示未注册。
    /// </summary>
    /// <param name="key">服务标识</param>
    /// <returns>Actor 引用或 null</returns>
    public IAsyncDisposable? Find(string key) =>
        _services.TryGetValue(key, out var actor) ? actor : null;

    /// <summary>
    /// 注销服务 — 移除 key 关联,不 Dispose Actor(调用方负责)。
    /// </summary>
    /// <param name="key">服务标识</param>
    /// <returns>true=已移除,false=key 不存在</returns>
    public bool Unregister(string key) => _services.TryRemove(key, out _);

    /// <summary>已注册服务标识快照</summary>
    public string[] GetRegisteredKeys() => _services.Keys.ToArray();
}
