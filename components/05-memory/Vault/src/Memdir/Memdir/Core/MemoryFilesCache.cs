namespace Vault.Memdir.Memdir.Core;

/// <summary>
/// 记忆文件缓存实现 — 对齐 TS: resetGetMemoryFilesCache
/// </summary>
[Register(typeof(JoinCode.Abstractions.Interfaces.Cache.IMemoryFilesCache))]
public sealed partial class MemoryFilesCache : JoinCode.Abstractions.Interfaces.Cache.IMemoryFilesCache
{
    private readonly Dictionary<string, IReadOnlyList<string>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public void Clear() => _cache.Clear();

    public IReadOnlyList<string>? GetFiles(string path) => _cache.GetValueOrDefault(path);

    public void SetFiles(string path, IReadOnlyList<string> files) => _cache[path] = files;
}
