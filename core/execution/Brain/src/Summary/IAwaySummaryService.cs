
namespace Core.Summary;

public interface IAwaySummaryService
{
    Task MarkAwayAsync(CancellationToken cancellationToken = default);
    Task<AwaySummaryResult> GenerateSummaryAsync(CancellationToken cancellationToken = default);
    Task TrackEventAsync(AwayEvent awayEvent, CancellationToken cancellationToken = default);
    bool IsAway { get; }
    DateTime? AwaySince { get; }
}
