namespace Infrastructure.Cache;

/// <summary>
/// 图片路径缓存实现 — 对齐 TS: clearStoredImagePaths
/// </summary>
[Register(typeof(JoinCode.Abstractions.Interfaces.Cache.IImageStore))]
public sealed class ImageStore : JoinCode.Abstractions.Interfaces.Cache.IImageStore
{
    private readonly Dictionary<int, string> _paths = new();

    public void Clear() => _paths.Clear();

    public string? GetPath(int id) => _paths.GetValueOrDefault(id);

    public void SetPath(int id, string path) => _paths[id] = path;
}
