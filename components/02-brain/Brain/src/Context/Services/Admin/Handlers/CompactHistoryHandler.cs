using JoinCode.Abstractions.Attributes;

namespace Core.Context;

/// <summary>
/// 压缩历史操作处理器 — 对齐 TS: compact 后 SessionStart Hook
/// </summary>
[Register]
public sealed partial class CompactHistoryHandler : IChatAdminOperationHandler
{
    private readonly IChatPromptManager _promptManager;
    private readonly SessionHookHelper _hookHelper;
    private readonly IFileSystem? _fs;

    public CompactHistoryHandler(
        IChatPromptManager promptManager,
        SessionHookHelper hookHelper,
        IFileSystem? fs = null)
    {
        _promptManager = promptManager;
        _hookHelper = hookHelper;
        _fs = fs;
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

            _promptManager.ClearCache();
            await _promptManager.ClearRemindersAsync(ct).ConfigureAwait(false);

            var sessionId = (context.ContextManager is ChatContextManager cm) ? cm.SessionId : "default";
            await _hookHelper.ExecuteSessionStartHookAsync(sessionId, "compact", ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            context.Error = ex;
        }
    }
}
