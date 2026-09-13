namespace JoinCode.Abstractions.Interfaces;

public interface ITranscriptService
{
    Task AppendEntryAsync(string sessionId, TranscriptEntry entry, CancellationToken cancellationToken = default);

    Task AppendEntriesAsync(string sessionId, IReadOnlyList<TranscriptEntry> entries, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TranscriptEntry>> LoadTranscriptAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TranscriptSummary>> ListTranscriptsAsync(int limit = 20, CancellationToken cancellationToken = default);

    Task<bool> DeleteTranscriptAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<bool> TranscriptExistsAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存自定义标题 — 对齐 TS saveCustomTitle，追加 custom-title 元数据条目
    /// </summary>
    Task SaveCustomTitleAsync(string sessionId, string customTitle, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取自定义标题 — 对齐 TS，从 JSONL 中扫描最近的 custom-title 条目
    /// </summary>
    Task<string?> GetCustomTitleAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 对齐 TS recordContentReplacement — 持久化内容替换记录到 transcript
    /// TS: getProject().insertContentReplacement(replacements, agentId)
    /// 用于会话恢复时重建 ContentReplacementState，保证 prompt cache 一致性
    /// </summary>
    Task InsertContentReplacementAsync(string sessionId, IReadOnlyList<ContentReplacementRecord> records, CancellationToken cancellationToken = default);

    /// <summary>
    /// 对齐 TS loadTranscriptFile — 从 transcript 加载内容替换记录
    /// 用于会话恢复时重建 ContentReplacementState
    /// </summary>
    Task<IReadOnlyList<ContentReplacementRecord>> LoadContentReplacementsAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存会话信息到 {sessionId}/meta.json — 统一入口,替代 SessionData 直写
    /// </summary>
    Task SaveSessionInfoAsync(string sessionId, SessionInfo info, CancellationToken cancellationToken = default);

    /// <summary>
    /// 加载会话信息 — 不存在返回 null
    /// </summary>
    Task<SessionInfo?> GetSessionInfoAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 迁移旧扁平格式(.json 直接在 sessions 根目录)到每会话子目录格式 — 幂等,不删旧文件
    /// 修复路径重构后现有扁平 .json 扫不到的 regression
    /// </summary>
    Task MigrateLegacyAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// 会话信息 — 存储到 {sessionId}/meta.json,替代 SessionData 的非消息字段
/// CustomTitle 不在此处(通过 transcript entry Type="custom-title" 存储,GetCustomTitleAsync 读取)
/// </summary>
public sealed record SessionInfo
{
    public string Id { get; init; } = string.Empty;
    public string ProjectPath { get; init; } = string.Empty;
    public string ProjectName { get; init; } = string.Empty;
    public string BranchName { get; init; } = string.Empty;
    public string ModelId { get; init; } = string.Empty;
    public string Vendor { get; init; } = string.Empty;
    public DateTime CreatedAt { get; init; }
}
