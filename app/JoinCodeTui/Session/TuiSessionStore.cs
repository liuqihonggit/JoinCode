namespace JoinCode.Tui.Session;

/// <summary>
/// TUI 会话元数据存储（T6）— transcript 消息落盘已下沉到引擎 TranscriptPersistMiddleware
/// （三端统一增量写入 {sessionId}/transcript.json），本类只负责 meta.json 元数据写盘。
/// sessionId 复用 CLI SessionIdGenerator 可读格式；T7 会话切换将在此扩展列表/切换能力。
/// </summary>
internal sealed class TuiSessionStore
{
    private readonly ITranscriptService _transcriptService;

    /// <summary>当前会话 ID — T7 切换会话时更新</summary>
    public string SessionId { get; private set; }

    /// <summary>新会话序号偏移 — 同一分钟内连续开新会话时保证 ID 不冲突（T7）</summary>
    internal int NewSessionSequence { get; set; }

    public TuiSessionStore(
        ITranscriptService transcriptService,
        string? workingDirectory = null,
        DateTime? createdAt = null)
    {
        _transcriptService = transcriptService;
        SessionId = SessionIdGenerator.Generate(workingDirectory, createdAt);
    }

    /// <summary>
    /// 保存会话元信息到 meta.json — 启动时幂等调用，记录项目路径/模型/供应商。
    /// </summary>
    public async Task SaveMetaAsync(WorkflowConfig config, CancellationToken cancellationToken = default)
    {
        var projectPath = Environment.CurrentDirectory;
        await _transcriptService.SaveSessionInfoAsync(SessionId, new SessionInfo
        {
            Id = SessionId,
            ProjectPath = projectPath,
            ProjectName = Path.GetFileName(projectPath),
            ModelId = config.Provider?.ModelId ?? string.Empty,
            Vendor = config.Provider?.Vendor ?? string.Empty,
            CreatedAt = DateTime.UtcNow,
        }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 列出最近的会话摘要 — 供 /sessions 列表展示（T7）。
    /// </summary>
    public async Task<IReadOnlyList<TranscriptSummary>> ListSessionsAsync(int limit = 20, CancellationToken cancellationToken = default)
    {
        return await _transcriptService.ListTranscriptsAsync(limit, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 解析 /sessions 参数为目标会话 ID（T7）— 纯函数。
    /// 纯数字按 1-based 序号查列表；其余视为原始 sessionId 直通；越界/空列表返回 false。
    /// </summary>
    public static bool TryResolveTarget(string argument, IReadOnlyList<TranscriptSummary> summaries, out string targetSessionId)
    {
        targetSessionId = string.Empty;
        if (string.IsNullOrWhiteSpace(argument) || summaries.Count == 0)
            return false;

        if (int.TryParse(argument, out var index))
        {
            if (index < 1 || index > summaries.Count)
                return false;
            targetSessionId = summaries[index - 1].SessionId;
            return true;
        }

        targetSessionId = argument.Trim();
        return true;
    }

    /// <summary>
    /// 切换当前会话（T7）— 引擎内存桶 SwitchSession + 更新本地 SessionId；
    /// 此后引擎 TranscriptPersistMiddleware 自动续写目标会话文件（对齐 CLI --continue 语义）。
    /// 历史消息灌入由调用方编排（LoadSessionMessagesAsync 与 /resume 同链路）。
    /// </summary>
    public async Task SwitchToAsync(IChatContextManager contextManager, IChatService chatService, string targetSessionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetSessionId);

        contextManager.SwitchSession(targetSessionId);
        SessionId = targetSessionId;
        await Task.CompletedTask.ConfigureAwait(false);
    }
}
