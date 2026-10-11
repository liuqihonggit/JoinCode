namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 查询引擎门面 — 面向调用方的唯一公开入口，封装 IQueryService 和 IChatClient
/// 关系: 内部委托 IQueryService (01-ai) 执行实际 LLM 调用
/// </summary>
public interface IQueryEngine {
    /// <summary>执行查询并返回字符串结果。</summary>
    Task<string> ExecuteQueryAsync(string query, CancellationToken cancellationToken = default);

    /// <summary>流式查询。</summary>
    IAsyncEnumerable<QueryStreamChunk> QueryAsync(
        string userInput,
        MessageList chatHistory,
        CancellationToken cancellationToken = default);

    /// <summary>带选项的流式查询。</summary>
    IAsyncEnumerable<QueryStreamChunk> QueryAsync(
        string userInput,
        MessageList chatHistory,
        QueryOptions? options,
        CancellationToken cancellationToken = default);

    /// <summary>获取聊天补全服务。</summary>
    JoinCode.Abstractions.LLM.IQueryService GetChatCompletionService();

    /// <summary>获取聊天客户端内核。</summary>
    JoinCode.Abstractions.LLM.IChatClient GetKernel();
}