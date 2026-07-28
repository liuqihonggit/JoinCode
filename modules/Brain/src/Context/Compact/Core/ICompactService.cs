
namespace Core.Context.Compact;

public interface ICompactService
{
    Task<CompactResult> CompactAsync(CompactRequest request, CancellationToken cancellationToken = default);
    Task<CompactResult> PartialCompactAsync(PartialCompactRequest request, CancellationToken cancellationToken = default);
    bool ShouldAutoCompact(int currentTokenCount, int contextWindowTokens);
    bool ShouldSoftCompactNotice(int currentTokenCount, int contextWindowTokens);
    CompactWarningState CalculateWarningState(int currentTokenCount, int contextWindowTokens);
}

public sealed class CompactRequest
{
    public required IReadOnlyList<ApiMessage> Messages { get; init; }
    public CompactTrigger Trigger { get; init; } = CompactTrigger.Manual;
    public string? CustomInstructions { get; init; }
    public bool SuppressFollowUpQuestions { get; init; }
    public bool IsAutonomousMode { get; init; }
    public string? TranscriptPath { get; init; }
}

public sealed class PartialCompactRequest
{
    public required IReadOnlyList<ApiMessage> Messages { get; init; }
    public required int PivotIndex { get; init; }
    public CompactDirection Direction { get; init; } = CompactDirection.From;
    public string? CustomInstructions { get; init; }
    public string? UserFeedback { get; init; }
}

public sealed class CompactWarningState
{
    public required int PercentLeft { get; init; }
    public required bool IsAboveWarningThreshold { get; init; }
    public required bool IsAboveErrorThreshold { get; init; }
    public required bool IsAboveAutoCompactThreshold { get; init; }
    public required bool IsAtBlockingLimit { get; init; }
    public required bool IsAboveSoftCompactThreshold { get; init; }
}
