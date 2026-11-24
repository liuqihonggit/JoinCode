namespace JoinCode.Abstractions.Interfaces;

public interface ITranscriptService {
    /// <summary>异步追加单条会话记录。</summary>
    Task AppendEntryAsync(string sessionId, TranscriptEntry entry, CancellationToken cancellationToken = default);

    /// <summary>异步追加多条会话记录。</summary>
    Task AppendEntriesAsync(string sessionId, IReadOnlyList<TranscriptEntry> entries, CancellationToken cancellationToken = default);

    /// <summary>异步加载会话记录列表。</summary>
    Task<IReadOnlyList<TranscriptEntry>> LoadTranscriptAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>异步列出会话摘要列表。</summary>
    Task<IReadOnlyList<TranscriptSummary>> ListTranscriptsAsync(int limit = 20, CancellationToken cancellationToken = default);

    /// <summary>异步删除会话记录。</summary>
    Task<bool> DeleteTranscriptAsync(string sessionId, CancellationToken cancellationToken = default);

    /// <summary>异步判断会话记录是否存在。</summary>
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
    /// 确保会话实体存在（幂等）— 建会话目录 + <c>meta.json</c> + 空 <c>transcript.json</c>。
    /// 语义对齐「实体先于首条消息诞生」：新会话在首次提问前就能被列表/重命名/打开，与 DSH 契约一致。
    /// 已存在时只补缺失的 meta（不覆盖已有 meta，也不动已有 transcript）。
    /// </summary>
    /// <param name="sessionId">会话标识（引擎风格：32 位十六进制）</param>
    /// <param name="info">会话元数据（项目路径/供应商/模型/创建时间）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>本次是否新建了会话实体</returns>
    Task<bool> EnsureSessionAsync(string sessionId, SessionInfo info, CancellationToken cancellationToken = default);

    /// <summary>
    /// 流式旁路快照（ADR 0166 / TASK135 A1）— 回合未结束时把「当前已生成的全文」写到旁路文件，
    /// 打断/崩溃后 <see cref="LoadTranscriptAsync"/> 会自动收编最后一行，不再丢整段回复。
    /// 写方负责节流（默认 200ms 或 512 字符），本方法不做去重。
    /// **按查询分文件**：同一会话可有多条查询并行（ADR 0165），共用一份旁路会互相覆盖/互删。
    /// </summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="queryId">查询标识（ADR 0165）；null 表示无身份调用方，落到遗留单旁路文件。</param>
    /// <param name="turnSeq">会话内提交序号（TASK135 A2）；收编后的残稿按它与主记录一起排序。</param>
    /// <param name="content">当前已生成的累积全文（不是增量）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task AppendStreamingSnapshotAsync(string sessionId, string? queryId, int? turnSeq, string content, CancellationToken cancellationToken = default);

    /// <summary>
    /// 清理流式旁路（ADR 0166）— 回合正常落盘后调用；文件不存在时是 no-op。
    /// 异常/取消路径**不要**调用：旁路要留在磁盘上等下次加载收编。
    /// 只清本查询自己的旁路，不得影响同会话其他在途查询（TASK135 A1）。
    /// </summary>
    /// <param name="sessionId">会话标识。</param>
    /// <param name="queryId">查询标识（ADR 0165）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task ClearStreamingAsync(string sessionId, string? queryId, CancellationToken cancellationToken = default);

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
public sealed record SessionInfo {
    /// <summary>获取会话标识。</summary>
    public string Id { get; init; } = string.Empty;
    /// <summary>获取项目路径。</summary>
    public string ProjectPath { get; init; } = string.Empty;
    /// <summary>获取项目名称。</summary>
    public string ProjectName { get; init; } = string.Empty;
    /// <summary>获取分支名称。</summary>
    public string BranchName { get; init; } = string.Empty;
    /// <summary>获取模型标识。</summary>
    public string ModelId { get; init; } = string.Empty;
    /// <summary>获取供应商名称。</summary>
    public string Vendor { get; init; } = string.Empty;
    /// <summary>获取创建时间。</summary>
    public DateTime CreatedAt { get; init; }
}