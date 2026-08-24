using JoinCode.Abstractions.Attributes;

namespace Core.Context;

/// <summary>
/// 压缩历史操作处理器 — 对齐 TS: compact 后 SessionStart Hook
/// </summary>
[Register(typeof(IChatAdminOperationHandler), ServiceLifetime.Singleton)]
public sealed partial class CompactHistoryHandler : ServiceEntity, IChatAdminOperationHandler
{
    private readonly IChatPromptManager _promptManager;
    private readonly SessionHookHelper _hookHelper;
    private readonly IFileSystem? _fs;
    private readonly ITodoService? _todoService;
    private readonly ILogger<CompactHistoryHandler>? _logger;

    public CompactHistoryHandler(
        IChatPromptManager promptManager,
        SessionHookHelper hookHelper,
        IFileSystem? fs = null,
        ITodoService? todoService = null,
        ILogger<CompactHistoryHandler>? logger = null)
    {
        _promptManager = promptManager;
        _hookHelper = hookHelper;
        _fs = fs;
        _todoService = todoService;
        _logger = logger;
    }

    public ChatAdminOperation Operation => ChatAdminOperation.CompactHistory;

    public async Task ExecuteAsync(ChatAdminContext context, CancellationToken ct)
    {
        try
        {
            var staticPrefix = await _promptManager.GetStaticPrefixAsync().ConfigureAwait(false);

            await context.ContextManager.ClearMessagesAsync(ct).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(staticPrefix))
            {
                await context.ContextManager.UpdateSystemPromptAsync(staticPrefix, ct).ConfigureAwait(false);
            }

            var skillAttachment = context.ToolUseContext?.BuildInvokedSkillsAttachment();
            var compactSummary = skillAttachment is not null
                ? $"{context.Summary}\n\n{skillAttachment}"
                : context.Summary;

            await context.ContextManager.AddCompactSummaryMessageAsync(
                $"[上下文压缩摘要]\n{compactSummary}",
                ct).ConfigureAwait(false);

            if (_fs is not null && context.ToolUseContext?.RecentlyReadFiles.Count > 0)
            {
                var fileAttachments = await context.ToolUseContext
                    .BuildPostCompactFileAttachmentsAsync(_fs, cancellationToken: ct)
                    .ConfigureAwait(false);

                if (!string.IsNullOrEmpty(fileAttachments))
                {
                    await context.ContextManager.AddSystemMessageAsync(
                        $"[最近读取的文件]\n{fileAttachments}",
                        ct).ConfigureAwait(false);
                }
            }

            if (_todoService is not null)
            {
                await RestoreTodoProgressAsync(context.ContextManager, ct).ConfigureAwait(false);
            }

            _promptManager.ClearCache();
            await _promptManager.ClearRemindersAsync(ct).ConfigureAwait(false);

            var sessionId = (context.ContextManager is ChatContextManager cm) ? cm.SessionId : global::Core.Utils.SessionIdFactory.DefaultSessionId;
            await _hookHelper.ExecuteSessionStartHookAsync(sessionId, "compact", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            context.Error = ex;
        }
    }

    /// <summary>
    /// 压缩后恢复 TODO 任务进度 — 避免压缩导致任务追踪丢失
    /// </summary>
    private async Task RestoreTodoProgressAsync(IChatContextManager contextManager, CancellationToken ct)
    {
        try
        {
            var result = await _todoService!.ListTodosAsync(includeCompleted: true, cancellationToken: ct).ConfigureAwait(false);
            if (!result.Success || result.TotalCount == 0)
                return;

            var completedValue = TodoStatus.Completed.ToValue();
            var sb = new StringBuilder();
            sb.AppendLine($"[任务进度恢复] 共 {result.TotalCount} 项（已完成 {result.CompletedCount}，待处理 {result.PendingCount}）：");

            foreach (var todo in result.Todos)
            {
                var statusMarker = todo.Status.Equals(completedValue, StringComparison.OrdinalIgnoreCase) ? "[x]" : "[ ]";
                var content = todo.Content.Length > 100 ? todo.Content[..100] + "..." : todo.Content;
                sb.AppendLine($"  {statusMarker} {content} (优先级: {todo.Priority})");
            }

            await contextManager.AddSystemMessageAsync(sb.ToString(), ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[CompactHistoryHandler] TODO 任务进度恢复失败，不影响压缩主流程");
        }
    }
}
