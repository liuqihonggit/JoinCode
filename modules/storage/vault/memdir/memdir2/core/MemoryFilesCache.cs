namespace Vault.Memdir.Memdir.Core;

/// <summary>
/// 记忆文件缓存实现 — 对齐 TS: resetGetMemoryFilesCache
/// </summary>
[Register(typeof(JoinCode.Abstractions.Interfaces.Cache.IMemoryFilesCache), ServiceLifetime.Singleton)]
public sealed partial class MemoryFilesCache : ServiceEntity, JoinCode.Abstractions.Interfaces.Cache.IMemoryFilesCache {
    private readonly Dictionary<string, IReadOnlyList<string>> _cache = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc />
    public void Clear() => _cache.Clear();

    /// <inheritdoc />
    public IReadOnlyList<string>? GetFiles(string path) => _cache.GetValueOrDefault(path);

    /// <inheritdoc />
    public void SetFiles(string path, IReadOnlyList<string> files) => _cache[path] = files;
}