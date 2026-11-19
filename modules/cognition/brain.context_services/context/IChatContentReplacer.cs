namespace Core.Context;

/// <summary>
/// 内容替换器接口 — 超大工具结果持久化、per-message 预算
/// </summary>
public interface IChatContentReplacer {
    /// <summary>
    /// 初始化内容替换状态
    /// </summary>
    /// <param name="state">新建或重建的内容替换状态</param>
    /// <param name="initialMessages">初始消息列表（可选）</param>
    /// <returns>状态可用返回 true</returns>
    bool TryProvisionState(out ContentReplacementState? state, IReadOnlyList<ApiMessage>? initialMessages = null);

    /// <summary>
    /// 持久化超大工具结果
    /// </summary>
    ValueTask<string?> MaybePersistLargeToolResult(string toolName, string toolUseId, string content, string sessionId);

    /// <summary>
    /// 应用 per-message 预算
    /// </summary>
    Task<ContentReplacementResult> ApplyBudgetAsync(IReadOnlyList<ApiMessage> messages, ContentReplacementState state, string sessionId, CancellationToken cancellationToken = default);
}