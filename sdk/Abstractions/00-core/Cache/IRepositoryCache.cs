namespace JoinCode.Abstractions.Interfaces.Cache;

/// <summary>
/// 仓库检测缓存接口 — 对齐 TS: clearRepositoryCaches
/// </summary>
public interface IRepositoryCache
{
    /// <summary>
    /// 清除所有缓存
    /// </summary>
    void Clear();
}
