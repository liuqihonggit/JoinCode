using JoinCode.Cli;

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
}
