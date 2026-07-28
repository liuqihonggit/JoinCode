
namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 搜索服务接口，提供 Glob 和 Grep 搜索功能
/// </summary>
public interface ISearchService
{
    /// <summary>
    /// Glob 搜索 - 根据文件模式查找文件
    /// </summary>
    /// <param name="pattern">Glob 模式，如 "**/*.cs"</param>
    /// <param name="path">搜索路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>搜索结果</returns>
    Task<GlobSearchResult> GlobSearchAsync(
        string pattern,
        string? path = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Grep 搜索 - 在文件内容中搜索文本
    /// </summary>
    /// <param name="input">搜索参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>搜索结果</returns>
    Task<GrepSearchResult> GrepSearchAsync(
        GrepSearchInput input,
        CancellationToken cancellationToken = default);
}
