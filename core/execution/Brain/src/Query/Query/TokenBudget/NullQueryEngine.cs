
namespace Core.Query;

/// <summary>
/// 空查询引擎实现 - 用于 MCP 服务器模式下没有 Kernel 的情况
/// </summary>
public sealed class NullQueryEngine : IQueryEngine
{
    public IAsyncEnumerable<QueryStreamChunk> QueryAsync(string userInput, MessageList chatHistory, CancellationToken cancellationToken = default)
    {
        return AsyncEnumerable.Empty<QueryStreamChunk>();
    }

    public IAsyncEnumerable<QueryStreamChunk> QueryAsync(string userInput, MessageList chatHistory, QueryOptions? options, CancellationToken cancellationToken = default)
    {
        return AsyncEnumerable.Empty<QueryStreamChunk>();
    }

    public Task<string> ExecuteQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(string.Empty);
    }

    public IQueryService GetChatCompletionService()
    {
        throw new InvalidOperationException(ContractsErrorMessages.NullQueryEngineNotSupportChat);
    }

    public IChatClient GetKernel()
    {
        throw new InvalidOperationException(ContractsErrorMessages.NullQueryEngineNoKernel);
    }
}
