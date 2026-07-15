namespace JoinCode.Entry;

/// <summary>
/// 会话恢复中间件 — 处理 --continue 和 --resume CLI 参数
/// 在 SessionInitStep 之后执行，加载历史会话消息到 ChatService
/// 对齐 TS: claude --continue / claude --resume
/// </summary>
[Register]
internal sealed partial class SessionResumeStep : IMiddleware<StartupContext>
{
    public ErrorBehavior OnError => ErrorBehavior.Propagate;

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

        var fs = context.FileSystem;
        var sessionsDir = WorkflowConstants.Paths.SessionsDirectory;

        // 加载目标 SessionData
        var sessionData = options.ContinueSession
            ? await LoadMostRecentSessionAsync(sessionsDir, fs, ct).ConfigureAwait(false)
            : await LoadSessionByIdOrTitleAsync(sessionsDir, options.ResumeSessionId!, fs, ct).ConfigureAwait(false);

        if (sessionData is null)
        {
            var hint = options.ContinueSession
                ? "无历史会话可恢复，将启动新会话"
                : $"未找到会话: {options.ResumeSessionId}";
            Cli.TerminalHelper.WriteLine(hint);
            Diag.WriteLine($"[STEP] SessionResume: {hint}");
            await next(context, ct);
            return;
        }

        // 加载历史消息到 ChatService
        var chatService = context.Host.Services.GetRequiredService<IChatService>();
        var messages = sessionData.Messages.Select(m => new ApiMessageRecord
        {
            Role = m.Role,
            Content = m.Content
        }).ToList();

        await chatService.LoadSessionMessagesAsync(messages, ct).ConfigureAwait(false);

        var title = string.IsNullOrEmpty(sessionData.CustomTitle) ? sessionData.Id : sessionData.CustomTitle;
        Cli.TerminalHelper.WriteLine($"已恢复会话: {title} ({sessionData.Messages.Count} 条消息)");
        Diag.WriteLine($"[STEP] SessionResume: restored {sessionData.Id} with {sessionData.Messages.Count} messages");

        await next(context, ct);
    }

    /// <summary>
    /// 加载最近的会话（按 LastModified 排序）— 对齐 TS --continue 自动选择 last conversation
    /// </summary>
    private static async Task<SessionData?> LoadMostRecentSessionAsync(string sessionsDir, IFileSystem fs, CancellationToken ct)
    {
        if (!fs.DirectoryExists(sessionsDir))
            return null;

        var files = fs.GetFiles(sessionsDir, "*.json", SearchOption.TopDirectoryOnly);
        var mostRecent = files
            .Select(f => new { Path = f, LastModified = fs.GetLastWriteTime(f) })
            .OrderByDescending(x => x.LastModified)
            .FirstOrDefault();

        if (mostRecent is null)
            return null;

        return await ReadSessionDataAsync(mostRecent.Path, fs, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 按 sessionId 精确匹配或 customTitle 模糊匹配加载会话
    /// 对齐 TS: resume.tsx call 函数 — UUID → customTitle
    /// </summary>
    private static async Task<SessionData?> LoadSessionByIdOrTitleAsync(string sessionsDir, string searchTerm, IFileSystem fs, CancellationToken ct)
    {
        if (!fs.DirectoryExists(sessionsDir))
            return null;

        // 1. UUID 精确匹配
        var sessionPath = Path.Combine(sessionsDir, $"{searchTerm}.json");
        if (fs.FileExists(sessionPath))
        {
            return await ReadSessionDataAsync(sessionPath, fs, ct).ConfigureAwait(false);
        }

        // 2. customTitle 模糊匹配（大小写不敏感）
        var files = fs.GetFiles(sessionsDir, "*.json", SearchOption.TopDirectoryOnly);
        foreach (var file in files)
        {
            var data = await ReadSessionDataAsync(file, fs, ct).ConfigureAwait(false);
            if (data is null || string.IsNullOrEmpty(data.CustomTitle))
                continue;

            if (data.CustomTitle.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                return data;
        }

        return null;
    }

    /// <summary>
    /// 读取并反序列化会话文件
    /// </summary>
    private static async Task<SessionData?> ReadSessionDataAsync(string filePath, IFileSystem fs, CancellationToken ct)
    {
        try
        {
            var json = await fs.ReadAllTextAsync(filePath, ct).ConfigureAwait(false);
            return JsonSerializer.Deserialize(json, CliJsonContext.Default.SessionData);
        }
        catch (Exception ex)
        {
            Diag.WriteLine($"[STEP] SessionResume: failed to read {filePath}: {ex.Message}");
            return null;
        }
    }
}
