namespace Core.Context;

/// <summary>
/// 内容替换器接口 — 超大工具结果持久化、per-message 预算
/// </summary>
public interface IChatContentReplacer
{
    /// <summary>
    /// 初始化内容替换状态
    /// </summary>
    ContentReplacementState? ProvisionState(IReadOnlyList<ApiMessage>? initialMessages = null);

    /// <summary>
    /// 持久化超大工具结果
    /// </summary>
    string? MaybePersistLargeToolResult(string toolName, string toolUseId, string content, string sessionId);

    /// <summary>
    /// 应用 per-message 预算
    /// </summary>
    Task<ContentReplacementResult> ApplyBudgetAsync(IReadOnlyList<ApiMessage> messages, ContentReplacementState state, string sessionId, CancellationToken cancellationToken = default);
}
