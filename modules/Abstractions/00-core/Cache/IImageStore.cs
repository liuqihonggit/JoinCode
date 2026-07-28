namespace JoinCode.Abstractions.Interfaces.Cache;

/// <summary>
/// 图片路径缓存接口 — 对齐 TS: clearStoredImagePaths
/// </summary>
public interface IImageStore
{
    /// <summary>
    /// 清除所有缓存
    /// </summary>
    void Clear();
}
