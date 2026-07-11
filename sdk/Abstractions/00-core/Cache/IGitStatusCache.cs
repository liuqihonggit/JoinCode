namespace JoinCode.Abstractions.Interfaces.Cache;

/// <summary>
/// Git 状态缓存接口 — 对齐 TS: clearResolveGitDirCache
/// </summary>
public interface IGitStatusCache
{
    /// <summary>
    /// 清除所有缓存
    /// </summary>
    void Clear();
}
