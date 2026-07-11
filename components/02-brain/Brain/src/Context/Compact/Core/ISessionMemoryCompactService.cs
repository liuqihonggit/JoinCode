
namespace Core.Context.Compact;

public interface ISessionMemoryCompactService
{
    Task<CompactResult?> TrySessionMemoryCompactAsync(
        IReadOnlyList<ApiMessage> messages,
        int autoCompactThreshold = 0,
        CancellationToken cancellationToken = default);
    Task<bool> IsSessionMemoryAvailableAsync();
    Task<string?> GetSessionMemoryContentAsync();
    Task UpdateSessionMemoryAsync(string content, CancellationToken cancellationToken = default);
}

public sealed class SessionMemoryCompactConfig
{
    public int MinTokens { get; init; } = 10_000;
    public int MinTextBlockMessages { get; init; } = 5;
    public int MaxTokens { get; init; } = 40_000;
    public int MinMessageTokensToInit { get; init; } = 10_000;
    public int MinTokensBetweenUpdate { get; init; } = 5_000;
    public int ToolCallsBetweenUpdates { get; init; } = 3;
    public int MaxSectionLength { get; init; } = 2000;
    public int MaxTotalSessionMemoryTokens { get; init; } = 12_000;

    public static SessionMemoryCompactConfig Default { get; } = new();
}
