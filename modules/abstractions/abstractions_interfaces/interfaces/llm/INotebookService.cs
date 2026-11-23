namespace JoinCode.Abstractions.LLM.Chat;

/// <summary>
/// 上下文存档服务接口（ADR 0156 P4）— 折叠/压缩时自动存档原始内容，LLM 可按需查阅
/// </summary>
public interface IContextArchiveService {
    /// <summary>
    /// 存档折叠的原始消息 — 在 CompactInPlace 前调用，将被折叠的消息保存到磁盘
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="foldableMessages">被折叠的原始消息列表</param>
    /// <param name="summary">LLM 生成的摘要文本</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>生成的 fold ID（格式：fold-{timestamp}-{shortHash}）</returns>
    Task<string> SaveFoldArchiveAsync(
        string sessionId,
        IReadOnlyList<ApiMessage> foldableMessages,
        string summary,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出指定会话的所有存档条目
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>存档条目列表（包含 foldId、摘要预览、时间、消息数）</returns>
    Task<IReadOnlyList<ContextArchiveEntry>> ListArchivesAsync(
        string sessionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取指定存档的完整原始内容
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="foldId">存档唯一标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>存档的完整 JSON 内容；不存在时返回 null</returns>
    Task<string?> ReadArchiveAsync(
        string sessionId,
        string foldId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 搜索存档内容（匹配消息文本）
    /// </summary>
    /// <param name="sessionId">会话标识</param>
    /// <param name="keyword">搜索关键词</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>匹配的存档条目列表</returns>
    Task<IReadOnlyList<ContextArchiveEntry>> SearchArchivesAsync(
        string sessionId,
        string keyword,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// 上下文存档条目 — 列表和搜索结果的摘要信息
/// </summary>
public sealed class ContextArchiveEntry {
    /// <summary>获取存档唯一标识。</summary>
    public string FoldId { get; init; } = string.Empty;
    /// <summary>获取摘要预览（前 200 字符）。</summary>
    public string SummaryPreview { get; init; } = string.Empty;
    /// <summary>获取存档时间。</summary>
    public DateTimeOffset Timestamp { get; init; }
    /// <summary>获取被折叠的消息数。</summary>
    public int MessageCount { get; init; }
}
