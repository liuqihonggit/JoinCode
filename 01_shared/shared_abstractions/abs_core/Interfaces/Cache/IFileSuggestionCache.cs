namespace JoinCode.Abstractions.Interfaces.Cache;

/// <summary>
/// 文件建议缓存接口 — 对齐 TS: clearFileSuggestionCaches
/// </summary>
public interface IFileSuggestionCache
{
    /// <summary>
    /// 清除所有缓存
    /// </summary>
    void Clear();
}
