namespace Infrastructure.Cache;

/// <summary>
/// 仓库检测缓存实现 — 对齐 TS: clearRepositoryCaches
/// </summary>
[Register(typeof(JoinCode.Abstractions.Interfaces.Cache.IRepositoryCache))]
public sealed partial class RepositoryCache : JoinCode.Abstractions.Interfaces.Cache.IRepositoryCache
{
    private readonly Dictionary<string, bool> _cache = new(StringComparer.OrdinalIgnoreCase);

    public void Clear() => _cache.Clear();

    public bool? IsRepository(string path) => _cache.GetValueOrDefault(path);

    public void Set(string path, bool isRepo) => _cache[path] = isRepo;
}
