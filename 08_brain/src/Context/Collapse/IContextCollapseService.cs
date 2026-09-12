
namespace Core.Context.Collapse;

public interface IContextCollapseService
{
    Task<ContextCollapseResult> CollapseAsync(
        string content,
        ContextCollapseOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CollapsibleSegment>> IdentifyCollapsibleSegmentsAsync(
        string content,
        ContextCollapseOptions? options = null,
        CancellationToken cancellationToken = default);

    Task<string> GenerateSummaryAsync(
        CollapsibleSegment segment,
        ContextCollapseOptions? options = null,
        CancellationToken cancellationToken = default);
}
