
namespace Core.Context.Compact;

/// <summary>
/// 微压缩服务 — 对齐 TS microCompact.ts
/// 清除旧工具结果内容以节省 token，保留最近 N 个工具结果
/// </summary>
[Register]
public sealed partial class MicrocompactService : IMicrocompactService
{
    [Inject] private readonly IClockService _clock;
    private const int BytesPerToken = 4;
    private const int ImageMaxTokenSize = 2000;

    /// <summary>
    /// 可压缩的工具名集合 — 对齐 TS COMPACTABLE_TOOLS
    /// 使用枚举常量，避免硬编码字符串
    /// </summary>
    private static readonly HashSet<string> CompactableTools =
    [
        FileToolNameConstants.FileRead,       // "Read"
        ShellToolNameConstants.ShellExecute,  // "Bash"
        ShellToolNameConstants.Powershell,    // "PowerShell"
        SearchToolNameConstants.Grep,         // "Grep"
        SearchToolNameConstants.Glob,         // "Glob"
        WebToolNameConstants.WebSearch,       // "WebSearch"
        WebToolNameConstants.WebFetch,        // "WebFetch"
        FileToolNameConstants.FileEdit,       // "Edit"
        FileToolNameConstants.FileWrite,      // "Write"
    ];

    public MicrocompactResult CompactMessages(
        IReadOnlyList<ApiMessage> messages,
        IReadOnlySet<string>? compactableToolNames = null,
        int keepRecent = 5)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var compactableIds = CollectCompactableToolIds(messages, compactableToolNames);
        if (compactableIds.Count == 0)
        {
            return new MicrocompactResult
            {
                Messages = messages,
                ToolsCleared = 0,
                TokensSaved = 0,
                WasCompacted = false
            };
        }

        var keepRecentSafe = Math.Max(1, keepRecent);
        var keepSet = new HashSet<string>(compactableIds.Skip(Math.Max(0, compactableIds.Count - keepRecentSafe)), StringComparer.Ordinal);
        var clearSet = new HashSet<string>(compactableIds.Where(id => !keepSet.Contains(id)), StringComparer.Ordinal);

        if (clearSet.Count == 0)
        {
            return new MicrocompactResult
            {
                Messages = messages,
                ToolsCleared = 0,
                TokensSaved = 0,
                WasCompacted = false
            };
        }

        var (result, tokensSaved) = ClearToolResults(messages, clearSet);

        return new MicrocompactResult
        {
            Messages = result,
            ToolsCleared = clearSet.Count,
            TokensSaved = tokensSaved,
            WasCompacted = tokensSaved > 0
        };
    }

    public TimeBasedMicrocompactResult? TimeBasedCompact(
        IReadOnlyList<ApiMessage> messages,
        int gapThresholdMinutes = 60,
        int keepRecent = 5)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var lastAssistant = messages.LastOrDefault(m => m.Role == MessageRole.Assistant);
        if (lastAssistant is null)
        {
            return null;
        }

        var lastTimestamp = lastAssistant.ExtractTimestamp();
        if (lastTimestamp is null)
        {
            return null;
        }

        var gapMinutes = (_clock.GetUtcNow() - lastTimestamp.Value).TotalMinutes;
        if (gapMinutes < gapThresholdMinutes)
        {
            return null;
        }

        var compactableIds = CollectCompactableToolIds(messages, null);
        var keepRecentSafe = Math.Max(1, keepRecent);
        var keepSet = new HashSet<string>(compactableIds.Skip(Math.Max(0, compactableIds.Count - keepRecentSafe)), StringComparer.Ordinal);
        var clearSet = new HashSet<string>(compactableIds.Where(id => !keepSet.Contains(id)), StringComparer.Ordinal);

        if (clearSet.Count == 0)
        {
            return null;
        }

        var (result, tokensSaved) = ClearToolResults(messages, clearSet);

        if (tokensSaved == 0)
        {
            return null;
        }

        return new TimeBasedMicrocompactResult
        {
            Messages = result,
            GapMinutes = gapMinutes,
            ToolsCleared = clearSet.Count,
            ToolsKept = keepSet.Count,
            TokensSaved = tokensSaved
        };
    }

    /// <summary>
    /// 估算消息 token 数 — 对齐 TS calculateMessageTokens
    /// 处理文本内容、ContentBlocks（image/document 固定 2000 tokens）、tool_use 等
    /// </summary>
    public int EstimateMessageTokens(IReadOnlyList<ApiMessage> messages)
    {
        var totalTokens = 0;
        foreach (var msg in messages)
        {
            // 文本内容
            if (msg.Content is not null)
            {
                totalTokens += RoughTokenCount(msg.Content);
            }

            // 多模态内容块 — 对齐 TS: image 2000, document 2000
            if (msg.ContentBlocks is not null)
            {
                foreach (var block in msg.ContentBlocks)
                {
                    if (block.Type == ToolContentType.Image || block.Type == ToolContentType.Document)
                    {
                        totalTokens += ImageMaxTokenSize;
                    }
                    else if (block.Type == ToolContentType.Text && block.Text is not null)
                    {
                        totalTokens += RoughTokenCount(block.Text);
                    }
                }
            }

            // Assistant 消息中的 tool_use — 对齐 TS: name + input
            if (msg.Role == MessageRole.Assistant)
            {
                foreach (var (_, name) in msg.ExtractToolCalls())
                {
                    totalTokens += RoughTokenCount(name) + RoughTokenCount("{}"); // name + 空参数估算
                }
            }
        }

        return (int)Math.Ceiling(totalTokens * 4.0 / 3.0);
    }

    /// <summary>
    /// 清除工具结果 — 对齐 TS maybeTimeBasedMicrocompact 中的 block 替换逻辑
    /// 遍历 Tool 角色消息，将 clearSet 中的工具结果内容替换为 ToolResultClearedMessage
    /// </summary>
    private static (List<ApiMessage> Messages, int TokensSaved) ClearToolResults(
        IReadOnlyList<ApiMessage> messages,
        HashSet<string> clearSet)
    {
        var tokensSaved = 0;
        var result = new List<ApiMessage>(messages.Count);

        foreach (var msg in messages)
        {
            if (msg.Role != MessageRole.Tool || msg.Content is null)
            {
                result.Add(msg);
                continue;
            }

            var toolCallId = msg.ExtractToolCallId();
            if (toolCallId is null || !clearSet.Contains(toolCallId))
            {
                result.Add(msg);
                continue;
            }

            // 对齐 TS: block.content !== TIME_BASED_MC_CLEARED_MESSAGE — 跳过已清除的
            if (string.Equals(msg.Content, ContentReplacementConstants.ToolResultClearedMessage, StringComparison.Ordinal))
            {
                result.Add(msg);
                continue;
            }

            tokensSaved += RoughTokenCount(msg.Content);
            result.Add(new ApiMessage(msg.Role, ContentReplacementConstants.ToolResultClearedMessage, msg.Metadata, msg.ModelId, msg.TokenUsage));
        }

        return (result, tokensSaved);
    }

    /// <summary>
    /// 收集可压缩的工具调用 ID — 对齐 TS collectCompactableToolIds
    /// 使用 ApiMessageExtensions.ExtractToolCalls 统一提取
    /// </summary>
    private static List<string> CollectCompactableToolIds(
        IReadOnlyList<ApiMessage> messages,
        IReadOnlySet<string>? compactableToolNames)
    {
        var effectiveToolNames = compactableToolNames ?? CompactableTools;
        var ids = new List<string>();

        foreach (var msg in messages)
        {
            if (msg.Role != MessageRole.Assistant)
                continue;

            foreach (var (id, name) in msg.ExtractToolCalls())
            {
                if (effectiveToolNames.Contains(name))
                {
                    ids.Add(id);
                }
            }
        }

        return ids;
    }

    private static int RoughTokenCount(string text)
    {
        return Math.Max(1, text.Length / BytesPerToken);
    }
}
