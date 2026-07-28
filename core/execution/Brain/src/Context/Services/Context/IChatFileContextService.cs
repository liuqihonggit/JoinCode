namespace Core.Context;

/// <summary>
/// 文件上下文服务接口 — 文件路径提取、消息转储
/// </summary>
public interface IChatFileContextService
{
    /// <summary>
    /// 更新文件上下文（提取文件路径）
    /// </summary>
    void UpdateFileContext(string message);

    /// <summary>
    /// 转储消息列表（JCC_DUMP_MESSAGES=1）
    /// </summary>
    void DumpMessageList(IList<ApiMessage> messages, string sessionId, int conversationTurn, int toolCallIteration);
}
