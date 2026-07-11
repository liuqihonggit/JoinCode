namespace Api.LLM.QueryServices;

/// <summary>
/// 空 QueryService 实现 — 仅供 ApiRegistration.CreateEmptyKernel 测试场景使用
/// 不发起任何真实 API 请求；所有方法抛 NotSupportedException
/// 真实场景请通过 QueryServiceFactory 创建对应派生类
/// </summary>
internal sealed class EmptyQueryService : IQueryService
{
    public Task<IReadOnlyList<ApiMessage>> GetApiMessageContentsAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings = null,
        IChatClient? kernel = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("EmptyQueryService 不支持实际 API 调用。请通过 QueryServiceFactory 配置真实 Provider。");

    public IAsyncEnumerable<StreamEvent> GetStreamEventContentsAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings = null,
        IChatClient? kernel = null,
        CancellationToken cancellationToken = default)
        => throw new NotSupportedException("EmptyQueryService 不支持实际 API 调用。请通过 QueryServiceFactory 配置真实 Provider。");
}
