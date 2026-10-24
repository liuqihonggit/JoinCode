
namespace JoinCode.ChatCommands;

/// <summary>
/// /compact 命令 - 上下文压缩
/// 对齐 TS: src/commands/compact/compact.ts
/// </summary>
[ChatCommand(Name = ChatCommandNameEnumConstants.Compact, Description = "压缩对话上下文以节省 Token，可选自定义摘要指令", Usage = "/compact [自定义摘要指令]", Aliases = ["comp"], ArgumentHint = "<optional custom summarization instructions>", Category = ChatCommandCategory.Session, ExposeToMcp = true)]
[ChatCommandArg("instructions", Type = "string", Description = "自定义压缩摘要指令")]
public sealed class CompactCommand : ChatCommandBase {
    /// <summary>命令名称。</summary>
    public override string Name => ChatCommandNameEnumConstants.Compact;
    /// <summary>命令描述。</summary>
    public override string Description => "压缩对话上下文以节省 Token，可选自定义摘要指令";
    /// <summary>命令用法提示。</summary>
    public override string Usage => "/compact [自定义摘要指令]";
    /// <summary>命令别名列表。</summary>
    public override string[] Aliases => new[] { "comp" };
    /// <summary>命令参数提示文本。</summary>
    public override string ArgumentHint => "<optional custom summarization instructions>";

    /// <summary>
    /// 执行 /compact 命令，生成对话摘要并压缩上下文，遇到 prompt_too_long 自动回滚重试。
    /// </summary>
    /// <param name="context">命令执行上下文。</param>
    /// <returns>表示异步操作的任务，承载命令执行结果。</returns>
    public override async Task<ChatCommandResult> ExecuteAsync(ChatCommandContext context) {
        // 对齐 TS: customInstructions = args.trim()
        var customInstructions = GetNormalizedArgs(context);

        TerminalHelper.WriteLine("正在压缩上下文...");

        var history = await context.GetCommandServices().ChatService.GetMessageListAsync(context.CancellationToken).ConfigureAwait(false);
        var (messageCount, originalTokens) = CalculateOriginalMetrics(history);

        if (messageCount == 0) {
            TerminalHelper.WriteLine("没有对话内容可压缩");
            return ChatCommandResult.Continue();
        }

        // 对齐 TS: truncateHeadForPTLRetry — compact 自身 PTL 时丢弃最旧消息重试（最多 3 次）
        const int maxPtRetries = 3;
        var contextManager = context.Services?.GetService(typeof(IChatContextManager)) as IChatContextManager;

        for (var attempt = 0; attempt < maxPtRetries; attempt++) {
            try {
                var summary = await GenerateSummaryFromHistoryAsync(history, context, customInstructions).ConfigureAwait(false);
                var compressedTokens = (summary?.Length ?? 0) / 4;

                await context.GetCommandServices().ChatService.CompactHistoryAsync(summary ?? "[无摘要]", context.CancellationToken).ConfigureAwait(false);

                var fallbackData = new CompactSummaryData {
                    MessagesSummarized = messageCount,
                    Direction = CompactDirection.UpTo,
                    OriginalTokens = originalTokens,
                    CompressedTokens = compressedTokens,
                };

                TerminalHelper.WriteLine(new CompactSummaryRenderer().Render(fallbackData));
                // TASK070 C6 — 数据随结果透传宿主投影层（TUI bridge 发 compact_completed 事件；
                // CLI 直连路径忽略 Data，输出已由上方 Renderer 自渲染）
                return ChatCommandResult.ContinueWith(fallbackData);
            } catch (Exception ex) when (IsPromptTooLongError(ex.Message)) {
                // 对齐 TS: truncateHeadForPTLRetry — 无管理器可回滚或重试耗尽(attempt=maxPtRetries-1)时,
                // break 走下方"最终失败"降级路径(兜底再试 + HandleError 收敛),禁止第 3 次 PTL 异常冒泡传播 REPL
                if (contextManager is null || attempt >= maxPtRetries - 1) break;

                // 对齐 TS: PTL 重试 — 回滚最近一轮消息后重试
                var rewindResult = await contextManager.RewindLastTurnAsync(context.CancellationToken).ConfigureAwait(false);
                if (rewindResult.RemovedCount == 0) break;
                TerminalHelper.WriteLine($"上下文过长，自动精简后重试... ({attempt + 1}/{maxPtRetries})");
            }
        }

        // 最终失败
        try {
            var summary = await GenerateSummaryFromHistoryAsync(history, context, customInstructions).ConfigureAwait(false);
            await context.GetCommandServices().ChatService.CompactHistoryAsync(summary ?? "[无摘要]", context.CancellationToken).ConfigureAwait(false);
            return ChatCommandResult.ContinueWith(new CompactSummaryData {
                MessagesSummarized = messageCount,
                Direction = CompactDirection.UpTo,
                OriginalTokens = originalTokens,
                CompressedTokens = (summary?.Length ?? 0) / 4,
            });
        } catch (Exception ex) {
            HandleError("生成摘要", ex);
        }

        return ChatCommandResult.Continue();
    }

    internal static (int Count, int EstimatedTokens) CalculateOriginalMetrics(IReadOnlyList<ApiMessageRecord> history) {
        var messageCount = history.Count;
        var totalChars = 0;
        foreach (var msg in history) {
            totalChars += msg.Content?.Length ?? 0;
        }
        var estimatedTokens = totalChars / 4;
        return (messageCount, estimatedTokens);
    }

    /// <summary>
    /// 使用结构化 Prompt 生成摘要 — 对齐 TS compactConversation
    /// </summary>
    private async Task<string> GenerateSummaryFromHistoryAsync(IReadOnlyList<ApiMessageRecord> history, ChatCommandContext context, string? customInstructions) {
        if (history.Count == 0) {
            return "[无对话内容可压缩]";
        }

        // 对齐 TS: 使用 9 段式结构化 prompt，替代简化 prompt
        var compactPrompt = Core.Prompts.Templates.Memory.CompactPromptTemplate.GetCompactPrompt(customInstructions);

        try {
            var rawSummary = await context.GetCommandServices().ChatService.SendMessageAsync(compactPrompt, context.CancellationToken).ConfigureAwait(false);
            return Core.Prompts.Templates.Memory.CompactPromptTemplate.FormatCompactSummary(rawSummary ?? "[无摘要]");
        } catch (Exception ex) {
            HandleError("生成摘要", ex);
            return "[无法生成摘要]";
        }
    }

    private static bool IsPromptTooLongError(string errorMessage) {
        return !string.IsNullOrEmpty(errorMessage)
            && (errorMessage.Contains("prompt_too_long", StringComparison.OrdinalIgnoreCase)
                || errorMessage.Contains("prompt too long", StringComparison.OrdinalIgnoreCase)
                || errorMessage.StartsWith("API Error: prompt_too_long", StringComparison.OrdinalIgnoreCase));
    }
}