
namespace JoinCode.ChatCommands;

/// <summary>
/// /clear 命令 - 清空聊天历史并清屏
/// 对齐 TS: src/commands/clear/clear.ts + conversation.ts + caches.ts
/// </summary>
[ChatCommand(Name = ChatCommandNameConstants.Clear, Description = "清空聊天历史并释放上下文", Usage = "/clear", Aliases = ["reset", "new", "cls"], Category = ChatCommandCategory.Session)]
public sealed partial class ClearCommand : ChatCommandBase
{
    [Inject] private readonly ILogger<ClearCommand>? _logger;

    public override string Name => ChatCommandNameConstants.Clear;
    public override string Description => "清空聊天历史并释放上下文";
    public override string Usage => "/clear";
    public override string[] Aliases => ["reset", "new", "cls"];
    public override string ArgumentHint => string.Empty;

    public ClearCommand(ILogger<ClearCommand>? logger = null)
    {
        _logger = logger;
    }

    public override async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context)
    {
        // 对齐 TS: clearConversation — 直接清除，无需确认
        // TS 没有 --force 标志，直接执行清除

        // 1. 执行 SessionEnd hooks（清除前）
        // 对齐 TS: executeSessionEndHooks('clear', ...)
        var hookManager = GetService<ISessionStartHookManager>(context);
        if (hookManager is not null)
        {
            try
            {
                var hookContext = new SessionStartHookContext
                {
                    SessionId = context.SessionId,
                    Source = "clear"
                };
                await hookManager.OnSessionStartAsync(hookContext, context.CancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "SessionEnd hook 执行失败，继续清除");
            }
        }

        // 2. 清空聊天历史
        await context.Services.ChatService.ClearHistoryAsync(context.CancellationToken).ConfigureAwait(false);
        _logger?.LogInformation("聊天历史已清空");

        // 3. 清屏
        context.ClearScreen?.Invoke();

        // 4. 清除会话缓存
        // 对齐 TS: clearSessionCaches
        ClearSessionCaches(context);

        // 5. 异步清除思考存储
        var thinkingStore = context.Services.ThinkingStore;
        if (thinkingStore is not null)
        {
            try
            {
                await thinkingStore.ClearAsync(context.SessionId, context.CancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "思考存储清除失败");
            }
        }

        // 6. 清除 Hook 配置缓存
        var hookConfigManager = context.Services.HookConfigurationManager;
        if (hookConfigManager is not null)
        {
            try
            {
                await hookConfigManager.InvalidateCacheAsync(context.CancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "Hook 配置缓存清除失败");
            }
        }

        // 7. 清除文件操作追踪
        var fileOpTracker = context.Services.FileOperationTracker;
        if (fileOpTracker is not null)
        {
            try
            {
                fileOpTracker.Clear();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "文件操作追踪清除失败");
            }
        }

        TerminalHelper.WriteLine($"{TerminalColors.Success}聊天历史已清空，上下文已释放{AnsiStyleConstants.Reset}");

        return ChatCommandResult.Continue();
    }

    /// <summary>
    /// 清除会话相关缓存
    /// 对齐 TS: clearSessionCaches — 清除上下文缓存、技能缓存、命令缓存等
    /// </summary>
    private static void ClearSessionCaches(ChatCommandContext context)
    {
        // 1. 通用缓存服务（含工具信息缓存等）
        var cacheService = GetService<ICacheService>(context);
        cacheService?.Clear();

        // 2. 文件读取状态缓存 — 对齐 TS: readFileState.clear()
        var fileStateCache = GetService<IFileStateCache>(context);
        fileStateCache?.Clear();

        // 3. LSP 诊断状态 — 对齐 TS: resetAllLSPDiagnosticState()
        //    ILspDiagnosticRegistry 是 internal，通过 ILspDiagnosticProvider 间接清除
        var lspProvider = GetService<JoinCode.Abstractions.Interfaces.Lsp.ILspDiagnosticProvider>(context);
        lspProvider?.ClearDeliveredForFile("*");

        // 4. 速率限制快照缓存
        context.Services.RateLimitTracker?.Clear();

        // 5. 工作区目录缓存
        context.Services.WorkspaceService?.Clear();

        // 6. Turn Diff 记录缓存
        context.Services.TurnDiffProvider?.Clear();

        // 7. 工具空闲提醒状态
        var idleReminder = GetService<IToolIdleReminderService>(context);
        idleReminder?.Reset();

        // 8. 输出循环检测状态
        var loopDetector = GetService<Core.Context.IOutputLoopDetector>(context);
        loopDetector?.Reset();

        // 9. 工具信息缓存 — 对齐 TS: clearToolSearchDescriptionCache()
        var toolCacheManager = GetService<ToolCacheManager>(context);
        toolCacheManager?.InvalidateAllCache();

        // 10. 方法名缓存（静态）
        MethodNameCache.Clear();

        // 11. 技能缓存 — 对齐 TS: clearInvokedSkills() + clearDynamicSkills()
        //    ISkillService.ReloadAsync() 在下次使用时自动重建

        // 12. 文件建议缓存 — 对齐 TS: clearFileSuggestionCaches
        var fileSuggestionCache = GetService<JoinCode.Abstractions.Interfaces.Cache.IFileSuggestionCache>(context);
        fileSuggestionCache?.Clear();

        // 13. 仓库检测缓存 — 对齐 TS: clearRepositoryCaches
        var repoCache = GetService<JoinCode.Abstractions.Interfaces.Cache.IRepositoryCache>(context);
        repoCache?.Clear();

        // 14. Git 状态缓存 — 对齐 TS: clearResolveGitDirCache
        var gitStatusCache = GetService<JoinCode.Abstractions.Interfaces.Cache.IGitStatusCache>(context);
        gitStatusCache?.Clear();

        // 15. 图片路径缓存 — 对齐 TS: clearStoredImagePaths
        var imageStore = GetService<JoinCode.Abstractions.Interfaces.Cache.IImageStore>(context);
        imageStore?.Clear();

        // 16. 会话环境变量缓存 — 对齐 TS: clearSessionEnvVars
        var sessionEnvVars = GetService<JoinCode.Abstractions.Interfaces.Cache.ISessionEnvVars>(context);
        sessionEnvVars?.Clear();

        // 17. WebFetch 缓存 — 对齐 TS: clearWebFetchCache
        context.Services.WebService?.ClearCache();

        // 18. Agent 定义缓存 — 对齐 TS: clearAgentDefinitionsCache
        var agentDefProvider = GetService<JoinCode.Abstractions.Interfaces.IAgentDefinitionProvider>(context);
        agentDefProvider?.ClearCache();

        // 19. 记忆文件缓存 — 对齐 TS: resetGetMemoryFilesCache
        var memoryFilesCache = GetService<JoinCode.Abstractions.Interfaces.Cache.IMemoryFilesCache>(context);
        memoryFilesCache?.Clear();

        // 20. 工具权限缓存 — 对齐 TS: IToolPermissionManager.ClearCache
        var toolPermManager = GetService<JoinCode.Abstractions.Interfaces.IToolPermissionManager>(context);
        toolPermManager?.ClearCache();
    }
}
