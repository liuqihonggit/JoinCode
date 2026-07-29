namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 查询引擎门面 — 面向调用方的唯一公开入口，封装 IQueryService 和 IChatClient
/// 关系: 内部委托 IQueryService (01-ai) 执行实际 LLM 调用
/// </summary>
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
