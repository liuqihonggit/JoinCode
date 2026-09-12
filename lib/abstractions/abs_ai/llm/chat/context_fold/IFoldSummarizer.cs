namespace JoinCode.Abstractions.LLM.Chat;

/// <summary>
/// 上下文折叠摘要器 — LLM 聊天专用的对话头部摘要化
/// 关系: 本接口是 IContextCompressor (00-core) 在 dialogue 类型上的特化
/// </summary>
public interface IFoldSummarizer
{
    Task<string> SummarizeForFoldAsync(
        IReadOnlyList<ApiMessage> headMessages,
        CancellationToken cancellationToken = default);
}
