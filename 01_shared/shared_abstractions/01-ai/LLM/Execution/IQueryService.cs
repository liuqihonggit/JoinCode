
namespace JoinCode.Abstractions.LLM;

/// <summary>
/// LLM 查询底层服务 — 直接与 LLM API 交互
/// 关系: IQueryEngine (02-brain) 是本接口的门面封装，面向调用方的唯一公开入口
/// </summary>
public interface IQueryService
{
    Task<IReadOnlyList<ApiMessage>> GetApiMessageContentsAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings = null,
        IChatClient? kernel = null,
        CancellationToken cancellationToken = default);

    IAsyncEnumerable<StreamEvent> GetStreamEventContentsAsync(
        MessageList chatHistory,
        ChatOptions? executionSettings = null,
        IChatClient? kernel = null,
        CancellationToken cancellationToken = default);
}
