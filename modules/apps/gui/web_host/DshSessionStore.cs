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
        return new DshSessionListItem {
            SessionId = summary.SessionId,
            UpdatedAt = ToUnixMilliseconds(summary.LastModifiedAt),
            AgentAvailable = true,
            Running = false,
            Blank = summary.MessageCount == 0,
            Cwd = info?.ProjectPath ?? "",
            Projections = ProjectValues(customTitle, info, summary.MessageCount),
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
    internal async Task<DshSessionFollowSnapshot?> SnapshotAsync(string sessionId, CancellationToken cancellationToken) {
        var read = await ReadAsync(sessionId, cancellationToken);
        if (read is not { } session) {
            return null;
        }
        var records = DshTranscriptProjector.Project(sessionId, session.Entries);
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
        return (ProjectValues(customTitle, info, entries.Count), info, entries);
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

    /// <summary>投影键值（标题 / 代理预设 / 模型选择 / 列表元数据）</summary>
    private static DshSessionProjections ProjectValues(string? customTitle, SessionInfo? info, int messageCount) {
        var blank = messageCount == 0;
        return new DshSessionProjections {
            AsOfSeq = messageCount,
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

    /// <summary>一次投影/列表返回的最大会话条数（前端空 cursor 即从头拉取）</summary>
    internal const int MaxSessions = 200;
}
