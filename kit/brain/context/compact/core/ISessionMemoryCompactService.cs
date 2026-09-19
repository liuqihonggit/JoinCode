
namespace Core.Context.Compact;

/// <summary>
/// 会话记忆压缩服务接口 — 基于持久化会话记忆文件执行压缩
/// </summary>
public interface ISessionMemoryCompactService {
    /// <summary>
    /// 尝试基于会话记忆执行压缩
    /// </summary>
    /// <param name="messages">原始消息列表</param>
    /// <param name="autoCompactThreshold">自动压缩阈值，0 表示不检查</param>
    /// <param name="transcriptPath">可选的转录文件路径</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>压缩结果；不满足条件时返回 null</returns>
    Task<CompactResult?> TrySessionMemoryCompactAsync(
        IReadOnlyList<ApiMessage> messages,
        int autoCompactThreshold = 0,
        string? transcriptPath = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 检查会话记忆是否可用
    /// </summary>
    /// <returns>可用返回 true，否则 false</returns>
    Task<bool> IsSessionMemoryAvailableAsync();

    /// <summary>
    /// 获取会话记忆内容
    /// </summary>
    /// <returns>会话记忆内容；不存在时返回 null</returns>
    Task<string?> GetSessionMemoryContentAsync();

    /// <summary>
    /// 更新会话记忆内容
    /// </summary>
    /// <param name="content">新的会话记忆内容</param>
    /// <param name="cancellationToken">取消令牌</param>
    Task UpdateSessionMemoryAsync(string content, CancellationToken cancellationToken = default);
}

/// <summary>
/// 会话记忆压缩配置
/// </summary>
public sealed class SessionMemoryCompactConfig {
    /// <summary>触发压缩的最小 token 数</summary>
    public int MinTokens { get; init; } = 10_000;
    /// <summary>触发压缩的最小文本块消息数</summary>
    public int MinTextBlockMessages { get; init; } = 5;
    /// <summary>保留消息的最大 token 数</summary>
    public int MaxTokens { get; init; } = 40_000;
    /// <summary>初始化提取的最小消息 token 数</summary>
    public int MinMessageTokensToInit { get; init; } = 10_000;
    /// <summary>两次更新之间的最小 token 间隔</summary>
    public int MinTokensBetweenUpdate { get; init; } = 5_000;
    /// <summary>两次更新之间的工具调用次数</summary>
    public int ToolCallsBetweenUpdates { get; init; } = 3;
    /// <summary>单个章节最大长度</summary>
    public int MaxSectionLength { get; init; } = 2000;
    /// <summary>会话记忆最大总 token 数</summary>
    public int MaxTotalSessionMemoryTokens { get; init; } = 12_000;

    /// <summary>默认配置实例</summary>
    public static SessionMemoryCompactConfig Default { get; } = new();
}