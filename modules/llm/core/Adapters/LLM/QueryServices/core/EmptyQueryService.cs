namespace Api.LLM.QueryServices;

/// <summary>
/// 空 QueryService 实现 — 仅供 ServiceRegistration.CreateEmptyKernel 测试场景使用
/// 不发起任何真实 API 请求；所有方法抛 NotSupportedException
/// 真实场景请通过 QueryServiceFactory 创建对应派生类
/// </summary>
internal sealed class EmptyQueryService : IQueryService {
    /// <summary>获取 API 消息内容 — 空实现始终抛出异常。</summary>
    /// <param name="chatHistory">聊天历史。</param>
    /// <param name="executionSettings">执行设置。</param>
    /// <param name="kernel">聊天客户端。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>不支持，始终抛出异常。</returns>
    public Task<IReadOnlyList<ApiMessage>> GetApiMessageContentsAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings = null,
        IChatClient? kernel = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("[LLM001] EmptyQueryService 不支持实际 API 调用。请通过 QueryServiceFactory 配置真实 Provider。");

    /// <summary>获取流式事件内容 — 空实现始终抛出异常。</summary>
    /// <param name="chatHistory">聊天历史。</param>
    /// <param name="executionSettings">执行设置。</param>
    /// <param name="kernel">聊天客户端。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>不支持，始终抛出异常。</returns>
    public IAsyncEnumerable<StreamEvent> GetStreamEventContentsAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings = null,
        IChatClient? kernel = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("[LLM002] EmptyQueryService 不支持实际 API 调用。请通过 QueryServiceFactory 配置真实 Provider。");
}