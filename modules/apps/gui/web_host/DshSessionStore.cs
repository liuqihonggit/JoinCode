namespace JoinCode.Gui.WebHost;

/// <summary>
/// 会话注册表（ADR 0143 M2-b）：把自有引擎的会话存储（<see cref="ITranscriptService"/>，
/// 落盘 <c>~/.jcc/sessions/{sessionId}/</c>）投影成 DSH 前端认得的会话形状，作为会话的唯一数据源。
/// 同一份落盘数据先后由两个来源供给：启动即挂一个只读的轻量落盘实例（侧栏立刻有历史会话），
/// 引擎组装完成后热切换到引擎的会话存储实例（写法走引擎 actor，避免两个实例并发读改写同一文件）。
/// </summary>
internal sealed class DshSessionStore {
    /// <summary>当前会话存储（启动为轻量落盘实例，引擎就绪后切换为引擎实例）</summary>
    private ITranscriptService? _transcripts;

    /// <summary>注入会话存储 — 支持热切换（唯一数据源：同一份 <c>~/.jcc/sessions</c> 落盘数据）</summary>
    /// <param name="transcripts">会话存储（轻量落盘实例或引擎实例）</param>
    internal void AttachTranscripts(ITranscriptService transcripts) => _transcripts = transcripts;

    /// <summary>会话存储是否已就绪</summary>
    internal bool IsReady => _transcripts is not null;

    /// <summary>列出会话（投影 id / 更新时间 / 空白标记 / 工作目录 / 标题投影）</summary>
    /// <param name="limit">最多返回条数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>DSH 会话列表条目（引擎未就绪时为空）</returns>
    internal async Task<IReadOnlyList<DshSessionListItem>> ListAsync(int limit, CancellationToken cancellationToken) {
        if (_transcripts is not { } transcripts) {
            return [];
        }
        var summaries = await transcripts.ListTranscriptsAsync(limit, cancellationToken);
        var items = new List<DshSessionListItem>(summaries.Count);
        foreach (var summary in summaries) {
            items.Add(await ProjectAsync(transcripts, summary, cancellationToken));
        }
        return items;
    }

    /// <summary>列出会话 ID → 标题（供会话投影与工作区归属填充）</summary>
    /// <param name="limit">最多返回条数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>会话 ID 与标题（标题可能为 null，表示未命名）</returns>
    internal async Task<IReadOnlyList<(string SessionId, string? Title)>> ListTitlesAsync(int limit, CancellationToken cancellationToken) {
        var items = await ListAsync(limit, cancellationToken);
        return items.Select(item => (item.SessionId, item.Projections.Values.Title)).ToList();
    }

    /// <summary>重命名会话（写引擎 custom-title 条目）；会话不存在时返回 null</summary>
    /// <param name="sessionId">目标会话</param>
    /// <param name="title">新标题</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>标题与对应的会话序号（用于前端投影版本）；会话不存在返回 null</returns>
    internal async Task<(string Title, long Seq)?> RenameAsync(string sessionId, string title, CancellationToken cancellationToken) {
        if (_transcripts is not { } transcripts) {
            return null;
        }
        if (!await transcripts.TranscriptExistsAsync(sessionId, cancellationToken)) {
            return null;
        }
        await transcripts.SaveCustomTitleAsync(sessionId, title, cancellationToken);
        var entries = await transcripts.LoadTranscriptAsync(sessionId, cancellationToken);
        return (title, entries.Count);
    }

    /// <summary>把一条引擎会话摘要投影成 DSH 会话条目</summary>
    private static async Task<DshSessionListItem> ProjectAsync(
        ITranscriptService transcripts,
        TranscriptSummary summary,
        CancellationToken cancellationToken) {
        var customTitle = await transcripts.GetCustomTitleAsync(summary.SessionId, cancellationToken);
        var info = await transcripts.GetSessionInfoAsync(summary.SessionId, cancellationToken);
        var blank = summary.MessageCount == 0;
        return new DshSessionListItem {
            SessionId = summary.SessionId,
            UpdatedAt = ToUnixMilliseconds(summary.LastModifiedAt),
            AgentAvailable = true,
            Running = false,
            Blank = blank,
            Cwd = info?.ProjectPath ?? "",
            Projections = new DshSessionProjections {
                AsOfSeq = summary.MessageCount,
                Values = new DshSessionProjectionValues {
                    Title = customTitle,
                    AgentPreset = DefaultAgentPreset,
                    ModelSelection = new DshSessionModelSelectionProjection {
                        LastUsed = ToModelRef(info),
                        Next = null,
                    },
                    SessionListMetadata = new DshSessionListMetadata {
                        Blank = blank,
                        LastPromptAt = null,
                    },
                },
            },
        };
    }

    /// <summary>会话最近使用的模型（供应商与模型都缺失时返回 null，表示尚未确定）</summary>
    private static DshModelSelectionRef? ToModelRef(SessionInfo? info) =>
        info is null || string.IsNullOrEmpty(info.Vendor) || string.IsNullOrEmpty(info.ModelId)
            ? null
            : new DshModelSelectionRef { Provider = info.Vendor, Model = info.ModelId };

    /// <summary>把引擎时间转成 DSH 需要的 Unix 毫秒时间戳（引擎时间为 UTC）</summary>
    private static long ToUnixMilliseconds(DateTime value) =>
        new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)).ToUnixTimeMilliseconds();

    /// <summary>默认代理预设 — 与 agentPresets/list 返回的 standard 对齐</summary>
    internal const string DefaultAgentPreset = "standard";
}
