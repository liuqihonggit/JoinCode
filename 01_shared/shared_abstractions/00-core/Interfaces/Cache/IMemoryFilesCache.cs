namespace JoinCode.Abstractions.Interfaces.Cache;

/// <summary>
/// 记忆文件缓存接口 — 对齐 TS: resetGetMemoryFilesCache
/// </summary>
public interface IMemoryFilesCache
{
    /// <summary>
    /// 清除所有缓存
    /// </summary>
    void Clear();
}
