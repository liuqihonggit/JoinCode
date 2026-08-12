namespace JoinCode.Abstractions.State;

/// <summary>
/// 异步存储基接口 — 统一 CRUD 模式: GetAsync/SaveAsync/RemoveAsync
/// 派生接口可扩展额外方法（如 GetAllAsync、ExistsAsync 等）
/// </summary>
public interface IAsyncStore<TKey, TValue> where TKey : notnull
{
    /// <summary>按键获取</summary>
    Task<TValue?> GetAsync(TKey key, CancellationToken cancellationToken = default);

    /// <summary>保存（新增或更新）</summary>
    Task SaveAsync(TKey key, TValue value, CancellationToken cancellationToken = default);

    /// <summary>按键删除</summary>
    Task<bool> RemoveAsync(TKey key, CancellationToken cancellationToken = default);
}

/// <summary>
/// 存储标记接口 — 所有 Store 的共同类型，支持 IEnumerable&lt;IStore&gt; 统一解析
/// </summary>
public interface IStore;
