namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 异步注册表基接口 — 统一 Register/Unregister/Get/GetAll 模式
/// 派生接口可扩展额外方法（如 Execute、GetByCategory 等）
/// </summary>
public interface IAsyncRegistry<TKey, TValue> where TKey : notnull
{
    /// <summary>注册项</summary>
    Task RegisterAsync(TKey key, TValue value, CancellationToken cancellationToken = default);

    /// <summary>注销项</summary>
    Task<bool> UnregisterAsync(TKey key, CancellationToken cancellationToken = default);

    /// <summary>按键获取</summary>
    Task<TValue?> GetAsync(TKey key, CancellationToken cancellationToken = default);

    /// <summary>获取所有项</summary>
    Task<IReadOnlyDictionary<TKey, TValue>> GetAllAsync(CancellationToken cancellationToken = default);

    /// <summary>是否包含指定键</summary>
    Task<bool> ContainsAsync(TKey key, CancellationToken cancellationToken = default);
}

/// <summary>
/// 注册表标记接口 — 所有 Registry 的共同类型，支持 IEnumerable&lt;IRegistry&gt; 统一解析
/// </summary>
public interface IRegistry;
