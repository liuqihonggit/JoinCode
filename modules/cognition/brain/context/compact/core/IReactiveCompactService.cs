
namespace Core.Context.Compact;

/// <summary>
/// 响应式压缩服务接口 — 处理 prompt-too-long 等错误触发的压缩
/// </summary>
public interface IReactiveCompactService {
    /// <summary>
    /// 执行响应式压缩
    /// </summary>
    /// <param name="messages">原始消息列表</param>
    /// <param name="errorMessage">触发压缩的错误消息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>压缩结果</returns>
    Task<CompactResult> RunReactiveCompactAsync(
        IReadOnlyList<ApiMessage> messages,
        string errorMessage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 判断错误消息是否为 prompt-too-long 类型
    /// </summary>
    /// <param name="errorMessage">错误消息文本</param>
    /// <returns>是 prompt-too-long 错误返回 true，否则 false</returns>
    bool IsPromptTooLongError(string errorMessage);

    /// <summary>
    /// 从 prompt-too-long 错误消息中解析超出 token 数
    /// </summary>
    /// <param name="errorMessage">错误消息文本</param>
    /// <returns>超出的 token 数；无法解析时返回 null</returns>
    int? GetPromptTooLongTokenGap(string errorMessage);
}

/// <summary>
/// 消息分组服务接口 — 按 API 轮次将消息分组
/// </summary>
public interface IMessageGroupingService {
    /// <summary>
    /// 按 API 轮次分组消息
    /// </summary>
    /// <param name="messages">原始消息列表</param>
    /// <returns>分组后的消息列表，每组对应一个 API 轮次</returns>
    IReadOnlyList<IReadOnlyList<ApiMessage>> GroupMessagesByApiRound(IReadOnlyList<ApiMessage> messages);
}