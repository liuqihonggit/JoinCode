namespace Infrastructure.Cache;

/// <summary>
/// 图片路径缓存实现 — 对齐 TS: clearStoredImagePaths
/// </summary>
[Register(typeof(JoinCode.Abstractions.Interfaces.Cache.IImageStore), ServiceLifetime.Singleton)]
public sealed partial class ImageStore : ServiceEntity, JoinCode.Abstractions.Interfaces.Cache.IImageStore {
    private readonly Dictionary<int, string> _paths = new();

    /// <summary>
    /// 清空所有缓存的图片路径
    /// </summary>
    public void Clear() => _paths.Clear();

    /// <summary>
    /// 获取指定 ID 的图片路径
    /// </summary>
    /// <param name="id">图片 ID</param>
    /// <returns>图片路径，未缓存则返回 null</returns>
    public string? GetPath(int id) => _paths.GetValueOrDefault(id);

    /// <summary>
    /// 设置指定 ID 的图片路径
    /// </summary>
    /// <param name="id">图片 ID</param>
    /// <param name="path">图片路径</param>
    public void SetPath(int id, string path) => _paths[id] = path;
}