
namespace Core.Context.Compact;

public interface IReactiveCompactService
{
    Task<CompactResult> RunReactiveCompactAsync(
        IReadOnlyList<ApiMessage> messages,
        string errorMessage,
        CancellationToken cancellationToken = default);
    bool IsPromptTooLongError(string errorMessage);
    int? GetPromptTooLongTokenGap(string errorMessage);
}

public interface IMessageGroupingService
{
    IReadOnlyList<IReadOnlyList<ApiMessage>> GroupMessagesByApiRound(IReadOnlyList<ApiMessage> messages);
}
