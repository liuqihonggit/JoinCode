namespace Infrastructure.Cache;

/// <summary>
/// Git 状态缓存实现 — 对齐 TS: clearResolveGitDirCache
/// </summary>
[Register(typeof(JoinCode.Abstractions.Interfaces.Cache.IGitStatusCache))]
public sealed class GitStatusCache : JoinCode.Abstractions.Interfaces.Cache.IGitStatusCache
{
    private readonly Dictionary<string, string?> _resolveCache = new(StringComparer.OrdinalIgnoreCase);

    public void Clear() => _resolveCache.Clear();

    public string? GetGitDir(string path) => _resolveCache.GetValueOrDefault(path);

    public void SetGitDir(string path, string? gitDir) => _resolveCache[path] = gitDir;
}
