namespace Infrastructure.Cache;

/// <summary>
/// 仓库检测缓存实现 — 对齐 TS: clearRepositoryCaches
/// </summary>
[Register(typeof(JoinCode.Abstractions.Interfaces.Cache.IRepositoryCache), ServiceLifetime.Singleton)]
public sealed partial class RepositoryCache : ServiceEntity, JoinCode.Abstractions.Interfaces.Cache.IRepositoryCache
{
    private readonly Dictionary<string, bool> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public void Clear() => _cache.Clear();

    /// <inheritdoc/>
    public bool? IsRepository(string path) => _cache.GetValueOrDefault(path);

    /// <inheritdoc/>
    public void Set(string path, bool isRepo) => _cache[path] = isRepo;
}
