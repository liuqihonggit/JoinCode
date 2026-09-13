namespace Infrastructure.Cache;

/// <summary>
/// Git 状态缓存实现 — 对齐 TS: clearResolveGitDirCache
/// </summary>
[Register(typeof(JoinCode.Abstractions.Interfaces.Cache.IGitStatusCache), ServiceLifetime.Singleton)]
public sealed partial class GitStatusCache : ServiceEntity, JoinCode.Abstractions.Interfaces.Cache.IGitStatusCache
{
    private readonly Dictionary<string, string?> _resolveCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 清空所有缓存的 Git 目录解析结果
    /// </summary>
    public void Clear() => _resolveCache.Clear();

    /// <summary>
    /// 获取指定路径对应的 Git 目录
    /// </summary>
    /// <param name="path">查询路径</param>
    /// <returns>Git 目录路径，未缓存或不存在返回 null</returns>
    public string? GetGitDir(string path) => _resolveCache.GetValueOrDefault(path);

    /// <summary>
    /// 缓存指定路径对应的 Git 目录
    /// </summary>
    /// <param name="path">查询路径</param>
    /// <param name="gitDir">Git 目录路径，可为 null 表示不存在</param>
    public void SetGitDir(string path, string? gitDir) => _resolveCache[path] = gitDir;
}
