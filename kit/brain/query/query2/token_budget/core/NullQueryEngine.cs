
namespace Core.Query;

/// <summary>
/// 空查询引擎实现 - 用于 MCP 服务器模式下没有 Kernel 的情况
/// </summary>
public sealed class NullQueryEngine : IQueryEngine
{
    /// <summary>
    /// 执行查询（空实现） - 返回空流，不产生任何输出
    /// </summary>
    /// <param name="userInput">用户输入文本</param>
    /// <param name="chatHistory">聊天历史消息列表</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>空的查询流块异步枚举</returns>
    public IAsyncEnumerable<QueryStreamChunk> QueryAsync(string userInput, MessageList chatHistory, CancellationToken cancellationToken = default)
    {
        return AsyncEnumerable.Empty<QueryStreamChunk>();
    }

    /// <summary>
    /// 执行查询（空实现） - 返回空流，不产生任何输出
    /// </summary>
    /// <param name="userInput">用户输入文本</param>
    /// <param name="chatHistory">聊天历史消息列表</param>
    /// <param name="options">查询选项</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>空的查询流块异步枚举</returns>
    public IAsyncEnumerable<QueryStreamChunk> QueryAsync(string userInput, MessageList chatHistory, QueryOptions? options, CancellationToken cancellationToken = default)
    {
        return AsyncEnumerable.Empty<QueryStreamChunk>();
    }

    /// <summary>
    /// 执行查询并返回完整结果（空实现） - 始终返回空字符串
    /// </summary>
    /// <param name="query">查询文本</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>空字符串任务</returns>
    public Task<string> ExecuteQueryAsync(string query, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(string.Empty);
    }

    /// <summary>
    /// 获取聊天补全服务 - 空引擎不支持此操作，始终抛出异常
    /// </summary>
    /// <returns>此方法始终抛出 <see cref="InvalidOperationException"/> 异常</returns>
    /// <exception cref="InvalidOperationException">空查询引擎不支持聊天补全服务</exception>
    public IQueryService GetChatCompletionService()
    {
        throw new InvalidOperationException(ContractsErrorMessages.NullQueryEngineNotSupportChat);
    }

    /// <summary>
    /// 获取内核客户端 - 空引擎无内核，始终抛出异常
    /// </summary>
    /// <returns>此方法始终抛出 <see cref="InvalidOperationException"/> 异常</returns>
    /// <exception cref="InvalidOperationException">空查询引擎无内核实例</exception>
    public IChatClient GetKernel()
    {
        throw new InvalidOperationException(ContractsErrorMessages.NullQueryEngineNoKernel);
    }
}
