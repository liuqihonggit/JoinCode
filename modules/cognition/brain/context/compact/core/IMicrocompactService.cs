
namespace Core.Context.Compact;

/// <summary>
/// 微压缩服务接口 — 纯规则压缩，不调用 LLM
/// </summary>
public interface IMicrocompactService {
    /// <summary>
    /// 普通微压缩 — 清除旧工具结果内容，保留最近 N 个
    /// </summary>
    /// <param name="messages">原始消息列表</param>
    /// <param name="compactableToolNames">自定义可压缩工具名集合，null 则使用默认</param>
    /// <param name="keepRecent">保留最近 N 个工具结果不被清除</param>
    /// <returns>微压缩结果</returns>
    MicrocompactResult CompactMessages(
        IReadOnlyList<ApiMessage> messages,
        IReadOnlySet<string>? compactableToolNames = null,
        int keepRecent = 5);

    /// <summary>
    /// 时间间隔微压缩 — 当对话空闲超过阈值时触发
    /// </summary>
    /// <param name="messages">原始消息列表</param>
    /// <param name="gapThresholdMinutes">空闲时间阈值（分钟）</param>
    /// <param name="keepRecent">保留最近 N 个工具结果</param>
    /// <returns>时间间隔压缩结果；不满足条件时返回 null</returns>
    TimeBasedMicrocompactResult? TimeBasedCompact(
        IReadOnlyList<ApiMessage> messages,
        int gapThresholdMinutes = 60,
        int keepRecent = 5);

    /// <summary>
    /// 估算消息 token 数
    /// </summary>
    /// <param name="messages">消息列表</param>
    /// <returns>估算的 token 数</returns>
    int EstimateMessageTokens(IReadOnlyList<ApiMessage> messages);
}

/// <summary>
/// 微压缩结果
/// </summary>
public sealed class MicrocompactResult {
    /// <summary>压缩后的消息列表</summary>
    public required IReadOnlyList<ApiMessage> Messages { get; init; }
    /// <summary>清除的工具结果数</summary>
    public required int ToolsCleared { get; init; }
    /// <summary>节省的 token 数</summary>
    public required int TokensSaved { get; init; }
    /// <summary>是否实际执行了压缩</summary>
    public required bool WasCompacted { get; init; }
}

/// <summary>
/// 时间间隔微压缩结果
/// </summary>
public sealed class TimeBasedMicrocompactResult {
    /// <summary>压缩后的消息列表</summary>
    public required IReadOnlyList<ApiMessage> Messages { get; init; }
    /// <summary>空闲时间（分钟）</summary>
    public required double GapMinutes { get; init; }
    /// <summary>清除的工具结果数</summary>
    public required int ToolsCleared { get; init; }
    /// <summary>保留的工具结果数</summary>
    public required int ToolsKept { get; init; }
    /// <summary>节省的 token 数</summary>
    public required int TokensSaved { get; init; }
}