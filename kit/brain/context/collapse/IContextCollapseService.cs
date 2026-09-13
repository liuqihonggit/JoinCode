
namespace Core.Context.Collapse;

/// <summary>
/// 上下文折叠服务接口
/// </summary>
public interface IContextCollapseService
{
    /// <summary>
    /// 折叠指定内容中的可折叠段
    /// </summary>
    /// <param name="content">待折叠的原始内容</param>
    /// <param name="options">折叠选项，为 null 时使用平衡策略</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>折叠结果</returns>
    Task<ContextCollapseResult> CollapseAsync(
        string content,
        ContextCollapseOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 识别内容中所有可折叠的段
    /// </summary>
    /// <param name="content">原始内容</param>
    /// <param name="options">折叠选项，为 null 时使用平衡策略</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>可折叠段列表，按起始偏移升序排列</returns>
    Task<IReadOnlyList<CollapsibleSegment>> IdentifyCollapsibleSegmentsAsync(
        string content,
        ContextCollapseOptions? options = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 为指定段生成摘要
    /// </summary>
    /// <param name="segment">待生成摘要的可折叠段</param>
    /// <param name="options">折叠选项，为 null 时使用平衡策略</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>段摘要文本</returns>
    Task<string> GenerateSummaryAsync(
        CollapsibleSegment segment,
        ContextCollapseOptions? options = null,
        CancellationToken cancellationToken = default);
}
