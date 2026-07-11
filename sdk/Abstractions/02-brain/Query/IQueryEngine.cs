namespace JoinCode.Abstractions.Interfaces;

public interface IQueryEngine
{
    Task<string> ExecuteQueryAsync(string query, CancellationToken cancellationToken = default);

    IAsyncEnumerable<QueryStreamChunk> QueryAsync(
        string userInput,
        MessageList chatHistory,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<QueryStreamChunk> QueryAsync(
        string userInput,
        MessageList chatHistory,
        QueryOptions? options,
        CancellationToken cancellationToken = default);

    JoinCode.Abstractions.LLM.IQueryService GetChatCompletionService();

    JoinCode.Abstractions.LLM.IChatClient GetKernel();
}
