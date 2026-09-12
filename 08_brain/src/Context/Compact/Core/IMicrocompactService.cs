
namespace Core.Context.Compact;

public interface IMicrocompactService
{
    MicrocompactResult CompactMessages(
        IReadOnlyList<ApiMessage> messages,
        IReadOnlySet<string>? compactableToolNames = null,
        int keepRecent = 5);
    TimeBasedMicrocompactResult? TimeBasedCompact(
        IReadOnlyList<ApiMessage> messages,
        int gapThresholdMinutes = 60,
        int keepRecent = 5);
    int EstimateMessageTokens(IReadOnlyList<ApiMessage> messages);
}

public sealed class MicrocompactResult
{
    public required IReadOnlyList<ApiMessage> Messages { get; init; }
    public required int ToolsCleared { get; init; }
    public required int TokensSaved { get; init; }
    public required bool WasCompacted { get; init; }
}

public sealed class TimeBasedMicrocompactResult
{
    public required IReadOnlyList<ApiMessage> Messages { get; init; }
    public required double GapMinutes { get; init; }
    public required int ToolsCleared { get; init; }
    public required int ToolsKept { get; init; }
    public required int TokensSaved { get; init; }
}
