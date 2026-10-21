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

    /// <summary>宿主事件日志（subagentCatalog 投影回放源，TASK058 B8；null 时目录恒空）</summary>
    private readonly DshSessionJournal? _journal;

    /// <summary>构造会话注册表</summary>
    /// <param name="journal">宿主事件日志（可选 — subagentCatalog 回放）</param>
    internal DshSessionStore(DshSessionJournal? journal = null) => _journal = journal;

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

    /// <summary>把一条引擎会话摘要投影成 DSH 会话条目（含 journal 回放的子代理目录）</summary>
    private async Task<DshSessionListItem> ProjectAsync(
        ITranscriptService transcripts,
        TranscriptSummary summary,
        CancellationToken cancellationToken) {
        var customTitle = await transcripts.GetCustomTitleAsync(summary.SessionId, cancellationToken);
        var info = await transcripts.GetSessionInfoAsync(summary.SessionId, cancellationToken);
        var entries = await transcripts.LoadTranscriptAsync(summary.SessionId, cancellationToken);
        var journalEvents = _journal is null ? [] : await _journal.LoadAsync(summary.SessionId, cancellationToken);
        return new DshSessionListItem {
            SessionId = summary.SessionId,
            UpdatedAt = ToUnixMilliseconds(summary.LastModifiedAt),
            AgentAvailable = true,
            Running = false,
            Blank = entries.Count == 0,
            Cwd = info?.ProjectPath ?? "",
            Projections = ProjectValues(customTitle, info, entries, journalEvents),
        };
    }

    /// <summary>
    /// 单个会话的投影快照（供 <c>session/projections</c> 使用）；会话不存在返回 null。
    /// </summary>
    /// <param name="sessionId">目标会话</param>
    /// <param name="cancellationToken">取消令牌</param>
    internal async Task<DshSessionProjectionSnapshot?> ProjectionsAsync(string sessionId, CancellationToken cancellationToken) {
        var read = await ReadAsync(sessionId, cancellationToken);
        return read is null
            ? null
            : new DshSessionProjectionSnapshot { AsOfSeq = read.Value.Projections.AsOfSeq, Values = read.Value.Projections.Values };
    }

    /// <summary>
    /// 打开会话所需的快照（供 <c>session/follow</c> 的打开帧使用）：会话头 + 投影 + 历史事件页。
    /// 会话不存在或引擎未就绪返回 null。历史页把引擎转录投影成 DSH 事件（turn/step 脚手架 + 消息），
    /// 页尾光标为最后一条事件的 seq（无事件时为 -1，与前端 emptyCursor 一致）。
    /// </summary>
    /// <param name="sessionId">目标会话</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <param name="hub">事件流中枢（有宿主事件日志时以日志为准，保证序号与已推送事件一致）</param>
    internal async Task<DshSessionFollowSnapshot?> SnapshotAsync(string sessionId, CancellationToken cancellationToken, DshSessionStreamHub? hub = null) {
        var read = await ReadAsync(sessionId, cancellationToken);
        if (read is not { } session) {
            return null;
        }
        if (hub is not null) {
            await hub.EnsureLoadedAsync(sessionId, cancellationToken);
            if (hub.IsLoaded(sessionId) && hub.Events(sessionId).Length == 0) {
                await hub.SeedAsync(sessionId, DshTranscriptProjector.Project(sessionId, session.Entries));
            }
        }
        var records = Records(sessionId, session.Entries, hub);
        return new DshSessionFollowSnapshot {
            Header = new DshSessionHeader {
                Id = sessionId,
                CreatedAt = session.Info is { } info ? ToUnixMilliseconds(info.CreatedAt) : 0,
                Cwd = session.Info?.ProjectPath,
                AgentPreset = DefaultAgentPreset,
            },
            Cursor = records.Count == 0 ? DshSessionFollowSnapshot.EmptyCursor : records[^1].Event.Seq,
            Records = records,
            Projections = new DshSessionProjectionSnapshot {
                AsOfSeq = session.Projections.AsOfSeq,
                Values = session.Projections.Values,
            },
        };
    }

    /// <summary>
    /// 会话的历史事件页：优先取宿主事件日志（与已推送事件同一序号），没有日志时用引擎转录投影。
    /// </summary>
    /// <param name="sessionId">会话 ID</param>
    /// <param name="entries">引擎转录条目</param>
    /// <param name="hub">事件流中枢</param>
    private static List<DshSessionRecord> Records(string sessionId, IReadOnlyList<TranscriptEntry> entries, DshSessionStreamHub? hub) {
        var events = hub?.Events(sessionId) ?? [];
        return events.Length > 0
            ? [.. events.Select(sessionEvent => new DshSessionRecord { Event = sessionEvent })]
            : DshTranscriptProjector.Project(sessionId, entries);
    }

    /// <summary>
    /// 取一页历史记录（供前端向上翻页）：返回恰好结束在 <c>throughSeq</c>、最多 <c>maxMessages</c> 条的事件，
    /// 按 seq 升序。前端会断言页尾必须等于请求光标（<c>beforeSeq</c> 因此不参与截断）；
    /// 会话不存在返回 null。
    /// </summary>
    /// <param name="request">分页参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <param name="hub">事件流中枢（有宿主事件日志时以日志为准）</param>
    internal async Task<DshSessionPageResult?> PageAsync(DshSessionPageArgs request, CancellationToken cancellationToken, DshSessionStreamHub? hub = null) {
        if (request.Address is not { } address || !string.Equals(address.Kind, "session", StringComparison.Ordinal)) {
            return null;
        }
        var read = await ReadAsync(address.SessionId, cancellationToken);
        if (read is null) {
            return null;
        }
        if (hub is not null) {
            await hub.EnsureLoadedAsync(address.SessionId, cancellationToken);
        }
        var max = request.MaxMessages is > 0 and var limit ? limit : DefaultPageSize;
        var all = Records(address.SessionId, read.Value.Entries, hub);
        var older = all.Where(record => record.Event.Seq <= request.ThroughSeq).ToArray();
        var page = older.Skip(Math.Max(0, older.Length - max)).ToArray();
        return new DshSessionPageResult { Records = [.. page], HasMore = older.Length > page.Length };
    }

    /// <summary>
    /// 会话的历史对话（供发送前把上下文灌回引擎）：只取普通 user/assistant 消息。
    /// 会话不存在或引擎未就绪返回 null。
    /// </summary>
    /// <param name="sessionId">目标会话</param>
    /// <param name="cancellationToken">取消令牌</param>
    internal async Task<IReadOnlyList<(MessageRole Role, string Content)>?> HistoryAsync(string sessionId, CancellationToken cancellationToken) {
        var read = await ReadAsync(sessionId, cancellationToken);
        return read?.Entries
            .Where(entry => entry.Type is null && !entry.IsSidechain && entry.Role is "user" or "assistant" && !string.IsNullOrWhiteSpace(entry.Content))
            .Select(entry => (entry.Role == "user" ? MessageRole.User : MessageRole.Assistant, entry.Content))
            .ToArray();
    }

    /// <summary>
    /// 读一个会话的投影、元信息与转录条目（一次读盘供 projections / follow 两个入口共用）；会话不存在返回 null。
    /// </summary>
    /// <param name="sessionId">目标会话</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>投影块、元信息与转录条目（元信息可能缺失）</returns>
    private async Task<(DshSessionProjections Projections, SessionInfo? Info, IReadOnlyList<TranscriptEntry> Entries)?> ReadAsync(string sessionId, CancellationToken cancellationToken) {
        if (_transcripts is not { } transcripts) {
            return null;
        }
        if (!await transcripts.TranscriptExistsAsync(sessionId, cancellationToken)) {
            return null;
        }
        var customTitle = await transcripts.GetCustomTitleAsync(sessionId, cancellationToken);
        var info = await transcripts.GetSessionInfoAsync(sessionId, cancellationToken);
        var entries = await transcripts.LoadTranscriptAsync(sessionId, cancellationToken);
        var journalEvents = _journal is null ? [] : await _journal.LoadAsync(sessionId, cancellationToken);
        return (ProjectValues(customTitle, info, entries, journalEvents), info, entries);
    }

    /// <summary>
    /// 全部会话的投影映射（<c>session/control</c> 基线）；引擎未就绪或没有会话时为空。
    /// </summary>
    /// <param name="limit">最多返回条数</param>
    /// <param name="cancellationToken">取消令牌</param>
    internal async Task<Dictionary<string, DshSessionProjectionSnapshot>> ProjectionsMapAsync(int limit, CancellationToken cancellationToken) {
        var items = await ListAsync(limit, cancellationToken);
        return items.ToDictionary(
            item => item.SessionId,
            item => new DshSessionProjectionSnapshot { AsOfSeq = item.Projections.AsOfSeq, Values = item.Projections.Values },
            StringComparer.Ordinal);
    }

    /// <summary>
    /// 确保会话实体存在（落盘 <c>~/.jcc/sessions/{sessionId}/</c>）。
    /// DSH 语义是「实体先于首条消息诞生」：前端点「新会话」即创建，因此这里建 meta + 空 transcript，
    /// 让新会话立刻能进列表、能被重命名、重启后仍在。
    /// </summary>
    /// <param name="sessionId">指定的会话 ID（null/空则生成引擎风格 32 位十六进制 ID）</param>
    /// <param name="projectPath">会话所属工作目录</param>
    /// <param name="vendor">供应商（null 表示尚未确定）</param>
    /// <param name="modelId">模型（null 表示尚未确定）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>会话 ID 与代理预设；引擎未就绪返回 null</returns>
    internal async Task<(string SessionId, string AgentPreset)?> CreateAsync(
        string? sessionId,
        string projectPath,
        string? vendor,
        string? modelId,
        CancellationToken cancellationToken) {
        if (_transcripts is not { } transcripts) {
            return null;
        }
        var resolvedId = string.IsNullOrWhiteSpace(sessionId) ? Guid.NewGuid().ToString("N") : sessionId;
        var info = new SessionInfo {
            Id = resolvedId,
            ProjectPath = projectPath,
            ProjectName = Path.GetFileName(projectPath),
            Vendor = vendor ?? "",
            ModelId = modelId ?? "",
            CreatedAt = DateTime.UtcNow,
        };
        await transcripts.EnsureSessionAsync(resolvedId, info, cancellationToken);
        return (resolvedId, DefaultAgentPreset);
    }

    /// <summary>
    /// 分叉会话（TASK058 B7）：复制源会话 meta + 转录 + journal 事件到新 ID（v1 全量复制，atSeq 截断为深化项）。
    /// 源会话不受影响；新会话立即可被列表/打开/续聊。
    /// </summary>
    /// <param name="sourceSessionId">源会话</param>
    /// <param name="journal">宿主事件日志（journal 事件随分叉复制）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>新会话 ID；源会话不存在或存储未就绪返回 null</returns>
    internal async Task<string?> ForkAsync(string sourceSessionId, DshSessionJournal journal, CancellationToken cancellationToken) {
        if (_transcripts is not { } transcripts) {
            return null;
        }
        // meta 缺失但转录存在的历史会话（旧宿主写入/JSONL 迁移遗留）容错：构造最小 SessionInfo 继续分叉
        var sourceInfo = await transcripts.GetSessionInfoAsync(sourceSessionId, cancellationToken);
        if (sourceInfo is null) {
            if (!await transcripts.TranscriptExistsAsync(sourceSessionId, cancellationToken)) {
                return null;
            }
            sourceInfo = new SessionInfo { Id = sourceSessionId, CreatedAt = DateTime.UtcNow };
        }
        var newId = Guid.NewGuid().ToString("N");
        await transcripts.EnsureSessionAsync(newId, sourceInfo with {
            Id = newId,
            CreatedAt = DateTime.UtcNow,
        }, cancellationToken);
        var entries = await transcripts.LoadTranscriptAsync(sourceSessionId, cancellationToken);
        if (entries.Count > 0) {
            await transcripts.AppendEntriesAsync(newId, entries, cancellationToken);
        }
        var events = await journal.LoadAsync(sourceSessionId, cancellationToken);
        foreach (var journalEvent in events) {
            await journal.AppendAsync(newId, journalEvent, cancellationToken);
        }
        return newId;
    }

    /// <summary>投影键值（标题 / 代理预设 / 模型选择 / 列表元数据 / 子代理实例目录 — TASK058 B8）</summary>
    private static DshSessionProjections ProjectValues(string? customTitle, SessionInfo? info, IReadOnlyList<TranscriptEntry> entries, IReadOnlyList<DshSessionEvent>? journalEvents = null) {
        var lastPrompt = entries.LastOrDefault(entry => entry.Type is null && !entry.IsSidechain && entry.Role == "user");
        return new DshSessionProjections {
            AsOfSeq = entries.Count,
            Values = new DshSessionProjectionValues {
                Title = customTitle,
                AgentPreset = DefaultAgentPreset,
                ModelSelection = new DshSessionModelSelectionProjection {
                    LastUsed = ToModelRef(info),
                    Next = null,
                },
                SessionListMetadata = new DshSessionListMetadata {
                    Blank = entries.Count == 0,
                    LastPromptAt = lastPrompt is null ? null : ToUnixMilliseconds(lastPrompt.Timestamp),
                },
                SubagentCatalog = DshSubagentCatalogProjector.Project(journalEvents ?? []),
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

    private const int DefaultPageSize = 100;

    /// <summary>一次投影/列表返回的最大会话条数（前端空 cursor 即从头拉取）</summary>
    internal const int MaxSessions = 200;
}
