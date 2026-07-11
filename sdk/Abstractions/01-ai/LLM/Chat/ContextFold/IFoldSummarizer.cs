namespace JoinCode.Abstractions.LLM.Chat;

public interface IFoldSummarizer
{
    Task<string> SummarizeForFoldAsync(
        IReadOnlyList<ApiMessage> headMessages,
        CancellationToken cancellationToken = default);
}
