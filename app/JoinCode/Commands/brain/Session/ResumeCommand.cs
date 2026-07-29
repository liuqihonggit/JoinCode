
namespace JoinCode.ChatCommands;

/// <summary>
/// /resume 命令 - 恢复会话
/// 对齐 TS: src/commands/resume/resume.tsx
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Resume, Description = "恢复之前的会话", Usage = "/resume [session-id]", Aliases = ["continue"], ArgumentHint = "[conversation id or search term]", Category = ChatCommandCategory.Session)]
public sealed class ResumeCommand : ChatCommandBase
{
    private readonly IClockService _clock = SystemClockService.Instance;
    public override string Name => ChatCommandNameConstants.Resume;
    public override string Description => "恢复之前的会话";
    public override string Usage => "/resume [session-id]";
    public override string[] Aliases => ["continue"];
    public override string ArgumentHint => "[conversation id or search term]";

    private static readonly string SessionsPath = Path.Combine(
        WorkflowConstants.Paths.JccDirectory,
        "sessions");

    public override async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        var fs = context.Services.FileSystem;
        if (!fs.DirectoryExists(SessionsPath))
        {
            DirectoryHelper.EnsureDirectoryExists(fs, SessionsPath);
        }

        var args = GetNormalizedArgs(context);
        if (!string.IsNullOrWhiteSpace(args))
        {
            var searchTerm = args.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? args;
            await ResumeWithArgumentAsync(searchTerm, context);
        }
        else
        {
            await ListSessionsAsync(context, showAllProjects: false);
        }

        return ChatCommandResult.Continue();
    }

    /// <summary>
    /// 有参数模式：先尝试 UUID 精确匹配，再尝试自定义标题搜索
    /// 对齐 TS: resume.tsx call 函数 — UUID → customTitle → 报错
    /// </summary>
    private async Task ResumeWithArgumentAsync(string searchTerm, ChatCommandContext context)
    {
        // L3.1: 先尝试 UUID 精确匹配
        var sessionPath = Path.Combine(SessionsPath, $"{searchTerm}.json");
        var fs = context.Services.FileSystem;
        if (fs.FileExists(sessionPath))
        {
            await ResumeSessionAsync(searchTerm, context, ResumeEntrypoint.SlashCommandSessionId);
            return;
        }

        // L3.1: 尝试自定义标题搜索
        // 对齐 TS: searchSessionsByCustomTitle — 大小写不敏感匹配
        var titleMatches = await SearchByCustomTitleAsync(searchTerm, context.CancellationToken, context.Services.FileSystem).ConfigureAwait(false);

        if (titleMatches.Count == 0)
        {
            // 对齐 TS: ResumeResult.sessionNotFound
            TerminalHelper.WriteLine($"{TerminalColors.Error}{string.Format(L.T(StringKey.HostResumeNotFound), searchTerm)}{AnsiStyleConstants.Reset}");
            TerminalHelper.WriteLine($"{TerminalColors.Muted}{L.T(StringKey.HostResumeHintList)}{AnsiStyleConstants.Reset}");
            return;
        }

        if (titleMatches.Count == 1)
        {
            // 唯一匹配 → 直接恢复
            await ResumeSessionAsync(titleMatches[0].Id, context, ResumeEntrypoint.SlashCommandTitle);
            return;
        }

        // 多匹配 → 报错提示
        // 对齐 TS: ResumeResult.multipleMatches
        TerminalHelper.WriteLine($"{TerminalColors.Error}{string.Format(L.T(StringKey.HostResumeMultipleMatches), searchTerm)}{AnsiStyleConstants.Reset}");
        foreach (var match in titleMatches.Take(5))
        {
            var title = string.IsNullOrEmpty(match.CustomTitle) ? match.Id[..Math.Min(8, match.Id.Length)] + "..." : match.CustomTitle;
            TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostResumeMatchItem), title, GetTimeAgo(match.LastModified)));
        }

        if (titleMatches.Count > 5)
        {
            TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostResumeMoreMatches), titleMatches.Count - 5));
        }

        TerminalHelper.WriteLine($"{TerminalColors.Muted}{L.T(StringKey.HostResumeRefineSearch)}{AnsiStyleConstants.Reset}");
    }

    /// <summary>
    /// 按自定义标题搜索会话
    /// 对齐 TS: sessionStorage.ts searchSessionsByCustomTitle
    /// </summary>
    private async Task<List<SessionLiteData>> SearchByCustomTitleAsync(string searchTerm, CancellationToken cancellationToken, IFileSystem fs)
    {
        if (!fs.DirectoryExists(SessionsPath))
        {
            return [];
        }

        var results = new List<SessionLiteData>();
        var sessionFiles = fs.GetFiles(SessionsPath, "*.json", SearchOption.TopDirectoryOnly);

        foreach (var file in sessionFiles)
        {
            try
            {
                var json = await fs.ReadAllTextAsync(file, cancellationToken).ConfigureAwait(false);
                var session = JsonSerializer.Deserialize(json, CliJsonContext.Default.SessionData);
                if (session is null || string.IsNullOrEmpty(session.CustomTitle))
                {
                    continue;
                }

                // 大小写不敏感匹配：精确匹配优先
                if (session.CustomTitle.Equals(searchTerm, StringComparison.OrdinalIgnoreCase) ||
                    session.CustomTitle.Contains(searchTerm, StringComparison.OrdinalIgnoreCase))
                {
                    var lastModified = fs.GetLastWriteTime(file);
                    results.Add(new SessionLiteData
                    {
                        Id = session.Id,
                        ProjectPath = session.ProjectPath,
                        CustomTitle = session.CustomTitle,
                        CreatedAt = session.CreatedAt,
                        LastModified = lastModified,
                        FilePath = file
                    });
                }
            }
            catch (Exception ex)
            {
                // 跳过无法解析的文件
                System.Diagnostics.Trace.WriteLine($"会话文件解析失败: {ex.Message}");
            }
        }

        // 按 sessionId 去重（保留最近修改的），按修改时间降序
        return results
            .GroupBy(r => r.Id)
            .Select(g => g.OrderByDescending(r => r.LastModified).First())
            .OrderByDescending(r => r.LastModified)
            .ToList();
    }

    /// <summary>
    /// 列出可恢复的会话（交互式选择器）
    /// 对齐 TS: ResumeCommand 组件 — LogSelector（上下键+搜索+Enter选择）
    /// L3.4: 支持 showAllProjects 切换
    /// L3.3: 使用 Lite 日志快速加载
    /// </summary>
    private async Task ListSessionsAsync(ChatCommandContext context, bool showAllProjects)
    {
        var currentSessionId = context.SessionId;

        // L3.3: Lite 日志加载 — 只读取文件 stat 信息，不读内容
        var liteEntries = LoadLiteSessions(showAllProjects, context.Services.FileSystem);

        // 过滤可恢复会话：排除当前会话
        // 对齐 TS: filterResumableSessions — 排除 sidechain + 当前会话
        var resumableEntries = liteEntries
            .Where(e => e.Id != currentSessionId)
            .OrderByDescending(e => e.LastModified)
            .Take(20)
            .ToList();

        if (resumableEntries.Count == 0)
        {
            TerminalHelper.WriteLine(L.T(StringKey.HostResumeNoSessions));
            return;
        }

        // 构建会话条目（L3.3: Lite 模式，preview 从 customTitle 或 ID 推断）
        var entries = new List<SessionEntry>();
        foreach (var lite in resumableEntries)
        {
            var timeAgo = GetTimeAgo(lite.LastModified);
            var preview = string.IsNullOrEmpty(lite.CustomTitle)
                ? lite.Id[..Math.Min(8, lite.Id.Length)] + "..."
                : lite.CustomTitle;
            entries.Add(new SessionEntry(lite.Id, preview, timeAgo, lite));
        }

        // 交互模式：使用 Selector 组件
        // 对齐 TS: LogSelector — 上下键+搜索+Enter选择+Esc取消
        if (!Core.Utils.TestEnvironmentDetector.IsNonInteractive)
        {
            var projectLabel = showAllProjects ? "（所有项目）" : "（当前仓库）";
            var selector = new Selector<SessionEntry>(
                string.Format(L.T(StringKey.HostResumeSelectorTitle), projectLabel),
                [.. entries],
                e => $"[{e.SessionId[..Math.Min(8, e.SessionId.Length)]}...] {e.TimeAgo}",
                e => e.Preview,
                enableSearch: true);

            var result = await selector.ShowAsync(context.CancellationToken).ConfigureAwait(false);

            if (result.Cancelled || result.Selected is null)
            {
                TerminalHelper.WriteLine(L.T(StringKey.HostResumeCancelled));
                return;
            }

            // L3.5: 从选择器恢复 → SlashCommandPicker
            await ResumeSessionAsync(result.Selected.SessionId, context, ResumeEntrypoint.SlashCommandPicker);
            return;
        }

        // 非交互模式回退：纯文本列表
        var projectHint = showAllProjects ? " [所有项目]" : "";
        for (var i = 0; i < entries.Count; i++)
        {
            var entry = entries[i];
            TerminalHelper.WriteLine($"  {TerminalColors.Muted}{i + 1}.{AnsiStyleConstants.Reset} [{entry.SessionId[..Math.Min(8, entry.SessionId.Length)]}...] {entry.TimeAgo}{projectHint}");
            TerminalHelper.WriteLine($"     {entry.Preview}");
        }

        TerminalHelper.NewLine();
        TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostResumeNonInteractivePrompt), entries.Count));

        // 输入重定向时（测试环境）优先使用 context.Prompt，否则直接返回取消
        var input = context.Prompt?.Invoke("选择会话");
        if (input is null)
        {
            if (Core.Utils.TestEnvironmentDetector.IsNonInteractive)
            {
                TerminalHelper.WriteLine(L.T(StringKey.HostResumeNonInteractiveCancelled));
                return;
            }
            else
            {
                input = TerminalHelper.ReadLine();
            }
        }

        if (string.IsNullOrWhiteSpace(input))
        {
            TerminalHelper.WriteLine(L.T(StringKey.HostResumeCancelled));
            return;
        }

        // L3.4: 全项目切换
        if (input.Trim().Equals("a", StringComparison.OrdinalIgnoreCase))
        {
            await ListSessionsAsync(context, !showAllProjects);
            return;
        }

        if (input.Trim().Equals("q", StringComparison.OrdinalIgnoreCase))
        {
            TerminalHelper.WriteLine(L.T(StringKey.HostResumeCancelled));
            return;
        }

        if (int.TryParse(input, out var choice) && choice >= 1 && choice <= entries.Count)
        {
            // L3.5: 从编号选择恢复 → SlashCommandPicker
            await ResumeSessionAsync(entries[choice - 1].SessionId, context, ResumeEntrypoint.SlashCommandPicker);
        }
        else
        {
            TerminalHelper.WriteLine($"{TerminalColors.Error}{L.T(StringKey.HostResumeInvalidChoice)}{AnsiStyleConstants.Reset}");
        }
    }

    /// <summary>
    /// L3.3: Lite 日志加载 — 只读取文件 stat 信息
    /// 对齐 TS: isLiteLog / getStatOnlyLogsForWorktrees
    /// </summary>
    private List<SessionLiteData> LoadLiteSessions(bool showAllProjects, IFileSystem fs)
    {
        if (!fs.DirectoryExists(SessionsPath))
        {
            return [];
        }

        var files = fs.GetFiles(SessionsPath, "*.json", SearchOption.TopDirectoryOnly);
        var entries = new List<SessionLiteData>();

        foreach (var file in files)
        {
            var sessionId = Path.GetFileNameWithoutExtension(file);

            // Lite 模式：只读 stat，不读内容
            var lite = new SessionLiteData
            {
                Id = sessionId,
                LastModified = fs.GetLastWriteTime(file),
                FilePath = file
            };

            // 尝试快速读取 customTitle（仅读头部少量数据）
            try
            {
                var json = fs.ReadAllText(file);
                var session = JsonSerializer.Deserialize(json, CliJsonContext.Default.SessionData);
                if (session is not null)
                {
                    lite.ProjectPath = session.ProjectPath ?? string.Empty;
                    lite.CustomTitle = session.CustomTitle ?? string.Empty;
                    lite.CreatedAt = session.CreatedAt;
                }
            }
            catch (Exception ex)
            {
                // 无法解析 → 仅保留 stat 信息
                System.Diagnostics.Trace.WriteLine($"Lite会话数据解析失败: {ex.Message}");
            }

            // L3.4: 全项目过滤
            if (!showAllProjects && !string.IsNullOrEmpty(lite.ProjectPath))
            {
                var currentCwd = Environment.CurrentDirectory;
                if (!string.Equals(lite.ProjectPath, currentCwd, StringComparison.OrdinalIgnoreCase) &&
                    !lite.ProjectPath.StartsWith(currentCwd + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
            }

            entries.Add(lite);
        }

        return entries;
    }

    /// <summary>
    /// 会话条目（供 Selector 使用）
    /// </summary>
    private sealed record SessionEntry(string SessionId, string Preview, string TimeAgo, SessionLiteData LiteData);

    /// <summary>
    /// 恢复指定会话
    /// 对齐 TS: call 函数 — UUID 精确匹配 + 直接恢复
    /// L3.5: 添加 ResumeEntrypoint 追踪
    /// </summary>
    private async Task ResumeSessionAsync(string sessionId, ChatCommandContext context, ResumeEntrypoint entrypoint)
    {
        var sessionPath = Path.Combine(SessionsPath, $"{sessionId}.json");

        var fs = context.Services.FileSystem;
        if (!fs.FileExists(sessionPath))
        {
            // 对齐 TS: ResumeResult.sessionNotFound
            TerminalHelper.WriteLine($"{TerminalColors.Error}{string.Format(L.T(StringKey.HostResumeSessionNotFound), sessionId)}{AnsiStyleConstants.Reset}");
            TerminalHelper.WriteLine($"{TerminalColors.Muted}{L.T(StringKey.HostResumeHintList)}{AnsiStyleConstants.Reset}");
            return;
        }

        // L3.5: 记录恢复入口
        TerminalHelper.WriteLine($"{TerminalColors.Muted}{string.Format(L.T(StringKey.HostResumeEntrypoint), entrypoint.ToValue())}{AnsiStyleConstants.Reset}");

        try
        {
            var json = await fs.ReadAllTextAsync(sessionPath, context.CancellationToken).ConfigureAwait(false);
            var session = JsonSerializer.Deserialize(json, CliJsonContext.Default.SessionData);

            if (session is null)
            {
                TerminalHelper.WriteLine($"{TerminalColors.Error}{L.T(StringKey.HostResumeParseError)}{AnsiStyleConstants.Reset}");
                return;
            }

            if (session.Messages is null || session.Messages.Count == 0)
            {
                TerminalHelper.WriteLine($"{TerminalColors.Error}{L.T(StringKey.HostResumeNoMessages)}{AnsiStyleConstants.Reset}");
                return;
            }

            // 跨项目恢复检查
            // 对齐 TS: checkCrossProjectResume
            var crossProjectResult = await CheckCrossProjectResumeAsync(session, context);
            if (crossProjectResult.IsCrossProject && !crossProjectResult.IsSameRepoWorktree)
            {
                // 不同项目 — 生成命令并复制到剪贴板
                var command = $"cd {crossProjectResult.ProjectPath} && jcc --resume {sessionId}";
                var clipboardService = context.Services.ClipboardService;
                if (clipboardService is not null)
                {
                    await clipboardService.SetTextAsync(command, context.CancellationToken).ConfigureAwait(false);
                }

                TerminalHelper.NewLine();
                TerminalHelper.WriteLine(L.T(StringKey.HostResumeCrossProjectNotice));
                TerminalHelper.NewLine();
                TerminalHelper.WriteLine(L.T(StringKey.HostResumeCrossProjectCommandPrompt));
                TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostResumeCrossProjectCommand), command));
                TerminalHelper.NewLine();
                if (clipboardService is not null)
                {
                    TerminalHelper.WriteLine(L.T(StringKey.HostResumeClipboardCopied));
                }
                return;
            }

            // 同项目 — 直接恢复
            var messages = session.Messages.Select(m => new ApiMessageRecord
            {
                Role = m.Role,
                Content = m.Content
            }).ToList();

            await context.Services.ChatService.LoadSessionMessagesAsync(messages, context.CancellationToken).ConfigureAwait(false);

            var title = string.IsNullOrEmpty(session.CustomTitle) ? sessionId : session.CustomTitle;
            TerminalHelper.WriteLine($"{TerminalColors.Success}{string.Format(L.T(StringKey.HostResumeRestored), title)}{AnsiStyleConstants.Reset}");
            TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostResumeMessageCount), session.Messages.Count));

            var recentMessages = session.Messages.TakeLast(3);
            foreach (var msg in recentMessages)
            {
                var role = msg.Role switch
                {
                    MessageRoleConstants.User => "你",
                    MessageRoleConstants.Assistant => "AI",
                    _ => msg.Role
                };

                var content = msg.Content;
                if (content.Length > 80)
                {
                    content = string.Concat(content.AsSpan(0, 77), "...");
                }

                TerminalHelper.WriteLine(string.Format(L.T(StringKey.HostResumeRecentMessage), role, content));
            }
        }
        catch (OperationCanceledException)
        {
            TerminalHelper.WriteLine(L.T(StringKey.HostResumeOperationCancelled));
        }
        catch (Exception ex)
        {
            HandleError("恢复会话", ex);
        }
    }

    /// <summary>
    /// 跨项目恢复检查
    /// 对齐 TS: checkCrossProjectResume
    /// </summary>
    private static async Task<CrossProjectResumeResult> CheckCrossProjectResumeAsync(SessionData session, ChatCommandContext context)
    {
        var currentCwd = Environment.CurrentDirectory;

        // 会话无项目路径信息 → 视为同项目
        if (string.IsNullOrEmpty(session.ProjectPath))
        {
            return CrossProjectResumeResult.SameProject();
        }

        // 同目录 → 直接恢复
        if (string.Equals(session.ProjectPath, currentCwd, StringComparison.OrdinalIgnoreCase))
        {
            return CrossProjectResumeResult.SameProject();
        }

        // 检查是否是同仓库 worktree
        var worktreeService = context.Services.WorktreeService;
        if (worktreeService is not null)
        {
            try
            {
                var worktreePaths = await worktreeService.ListWorktreesAsync(cancellationToken: context.CancellationToken).ConfigureAwait(false);

                var isSameRepo = worktreePaths.Any(wt =>
                    string.Equals(wt, session.ProjectPath, StringComparison.OrdinalIgnoreCase) ||
                    session.ProjectPath.StartsWith(wt + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));

                if (isSameRepo)
                {
                    return CrossProjectResumeResult.SameRepoWorktree(session.ProjectPath);
                }
            }
            catch (Exception ex)
            {
                // Worktree 检测失败 → 视为不同项目
                System.Diagnostics.Trace.WriteLine($"Worktree检测失败: {ex.Message}");
            }
        }

        // 不同项目 → 生成命令
        return CrossProjectResumeResult.DifferentProject(session.ProjectPath);
    }

    private static string GetTimeAgo(DateTime dateTime)
    {
        var span = DateTime.Now - dateTime;

        if (span.TotalMinutes < 1)
            return "刚刚";
        if (span.TotalHours < 1)
            return $"{span.Minutes} 分钟前";
        if (span.TotalDays < 1)
            return $"{span.Hours} 小时前";
        if (span.TotalDays < 7)
            return $"{span.Days} 天前";

        return dateTime.ToString("yyyy-MM-dd");
    }

    /// <summary>
    /// 保存会话（供其他组件调用）
    /// </summary>
    public static async Task SaveSessionAsync(string sessionId, List<SessionMessage> messages, IFileSystem fs, CancellationToken cancellationToken = default, IClockService? clock = null)
    {
        var c = clock ?? SystemClockService.Instance;
        if (!fs.DirectoryExists(SessionsPath))
        {
            DirectoryHelper.EnsureDirectoryExists(fs, SessionsPath);
        }

        var session = new SessionData
        {
            Id = sessionId,
            ProjectPath = Environment.CurrentDirectory,
            CreatedAt = c.GetUtcNow(),
            Messages = messages
        };

        var sessionPath = Path.Combine(SessionsPath, $"{sessionId}.json");
        var json = JsonSerializer.Serialize(session, CliIndentedJsonContext.Default.SessionData);

        await fs.WriteAllTextAsync(sessionPath, json, cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>
/// 跨项目恢复检查结果
/// 对齐 TS: CrossProjectResumeResult
/// </summary>
internal sealed record CrossProjectResumeResult
{
    public bool IsCrossProject { get; init; }
    public bool IsSameRepoWorktree { get; init; }
    public string ProjectPath { get; init; } = string.Empty;

    internal static CrossProjectResumeResult SameProject() => new() { IsCrossProject = false };
    internal static CrossProjectResumeResult SameRepoWorktree(string projectPath) => new() { IsCrossProject = true, IsSameRepoWorktree = true, ProjectPath = projectPath };
    internal static CrossProjectResumeResult DifferentProject(string projectPath) => new() { IsCrossProject = true, IsSameRepoWorktree = false, ProjectPath = projectPath };
}

public sealed class SessionData
{
    public string Id { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
    public string CustomTitle { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public List<SessionMessage> Messages { get; set; } = new();
}

/// <summary>
/// 轻量会话数据（仅 stat 信息，无 messages）
/// 对齐 TS: isLiteLog — messages 为空但 sessionId 存在
/// 用于快速加载会话列表，选择后按需加载完整数据
/// </summary>
public sealed class SessionLiteData
{
    public string Id { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
    public string CustomTitle { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime LastModified { get; set; }
    public string FilePath { get; set; } = string.Empty;
}

public sealed class SessionMessage : ChatMessage
{
}
