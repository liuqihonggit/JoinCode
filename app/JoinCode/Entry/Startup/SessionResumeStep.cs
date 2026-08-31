namespace JoinCode.Entry;

/// <summary>
/// 会话恢复中间件 — 处理 --continue 和 --resume CLI 参数
/// 在 SessionInitStep 之后执行，加载历史会话消息到 ChatService
/// 对齐 TS: claude --continue / claude --resume
/// 统一入口: 通过 ITranscriptService 读取 {sessionId}/transcript.jsonl,不再直读 .json
/// </summary>
[Register(typeof(IMiddleware<StartupContext>), ServiceLifetime.Singleton)]
internal sealed partial class SessionResumeStep : ServiceEntity, IMiddleware<StartupContext>
{

    public async Task InvokeAsync(StartupContext context, MiddlewareDelegate<StartupContext> next, CancellationToken ct)
    {
        var options = context.Options;

        // 无 --continue 也无 --resume → 跳过
        if (!options.ContinueSession && string.IsNullOrEmpty(options.ResumeSessionId))
        {
            await next(context, ct);
            return;
        }

        var session = context.Session;
        if (session is null)
        {
            // Session 未初始化 — 无法恢复，但允许继续启动（不阻塞）
            Diag.WriteLine("[STEP] SessionResume skipped: Session not initialized");
            await next(context, ct);
            return;
        }

        var transcriptService = context.Host.Services.GetService<ITranscriptService>();
        if (transcriptService is null)
        {
            Diag.WriteLine("[STEP] SessionResume skipped: ITranscriptService not available");
            await next(context, ct);
            return;
        }

        // 找目标会话 ID
        var sessionId = options.ContinueSession
            ? await FindMostRecentSessionIdAsync(transcriptService, ct).ConfigureAwait(false)
            : await FindSessionByIdOrTitleAsync(transcriptService, options.ResumeSessionId ?? throw new InvalidOperationException("ResumeSessionId required"), ct).ConfigureAwait(false);

        if (sessionId is null)
        {
            var hint = options.ContinueSession
                ? "无历史会话可恢复，将启动新会话"
                : $"未找到会话: {options.ResumeSessionId}";
            Cli.TerminalHelper.WriteLine(hint);
            Diag.WriteLine($"[STEP] SessionResume: {hint}");
            await next(context, ct);
            return;
        }

        // 加载历史消息 — 过滤非消息条目(custom-title/content-replacement/agent-name 等 Type 非空)
        var entries = await transcriptService.LoadTranscriptAsync(sessionId, ct).ConfigureAwait(false);
        var messages = entries
            .Where(e => string.IsNullOrEmpty(e.Type) && (e.Role == "user" || e.Role == "assistant"))
            .Select(e => new ApiMessageRecord { Role = e.Role, Content = e.Content })
            .ToList();

        // T6：先切引擎桶再灌入 — 此前只 OverrideSessionId（写盘目标）而引擎桶仍是 default，
        // transcript 落盘下沉引擎管道后必须 SwitchSession 才能续写到恢复的会话文件
        context.Host.Services.GetService<IChatContextManager>()?.SwitchSession(sessionId);

        // 加载历史消息到 ChatService
        var chatService = context.Host.Services.GetRequiredService<IChatService>();
        await chatService.LoadSessionMessagesAsync(messages, ct).ConfigureAwait(false);

        session.OverrideSessionId(sessionId);

        var customTitle = await transcriptService.GetCustomTitleAsync(sessionId, ct).ConfigureAwait(false);
        var title = string.IsNullOrEmpty(customTitle) ? sessionId : customTitle;
        Cli.TerminalHelper.WriteLine($"已恢复会话: {title} ({messages.Count} 条消息)");
        Diag.WriteLine($"[STEP] SessionResume: restored {sessionId} with {messages.Count} messages");

        await next(context, ct);
    }

    /// <summary>
    /// 加载最近的会话 — 对齐 TS --continue 自动选择 last conversation
    /// </summary>
    private static async Task<string?> FindMostRecentSessionIdAsync(ITranscriptService service, CancellationToken ct)
    {
        var summaries = await service.ListTranscriptsAsync(limit: 1, ct).ConfigureAwait(false);
        return summaries.Count > 0 ? summaries[0].SessionId : null;
    }

    /// <summary>
    /// 按 sessionId 精确匹配或 customTitle 模糊匹配查找会话
    /// 对齐 TS: resume.tsx call 函数 — UUID → customTitle
    /// </summary>
    private static async Task<string?> FindSessionByIdOrTitleAsync(ITranscriptService service, string searchTerm, CancellationToken ct)
    {
        // 1. UUID 精确匹配
        try
        {
            if (await service.TranscriptExistsAsync(searchTerm, ct).ConfigureAwait(false))
            {
                return searchTerm;
            }
        }
        catch (ArgumentException)
        {
            // searchTerm 含非法字符，跳过精确匹配
            Diag.WriteLine($"[STEP] SessionResume: searchTerm '{searchTerm}' contains invalid chars, skip exact match");
        }

        // 2. customTitle 模糊匹配（大小写不敏感）
        var summaries = await service.ListTranscriptsAsync(limit: 100, ct).ConfigureAwait(false);
        foreach (var summary in summaries)
        {
            var title = await service.GetCustomTitleAsync(summary.SessionId, ct).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(title) && title.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
            {
                return summary.SessionId;
            }
        }

        return null;
    }
}
