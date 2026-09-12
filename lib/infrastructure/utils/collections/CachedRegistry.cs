namespace Core.Utils;

/// <summary>
/// 带缓存的注册表 — 继承 MapRegistry，启用 Canonical/Alias 跟踪
/// 保留为向后兼容别名，新代码可直接用 MapRegistry(trackCanonical: true)
/// </summary>
public sealed class CachedRegistry<TKey, TValue> : MapRegistry<TKey, TValue> where TKey : notnull
{
    public CachedRegistry(IEqualityComparer<TKey>? comparer = null)
        : base(comparer, trackCanonical: true)
    {
    }
}
