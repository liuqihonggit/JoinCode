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
}
