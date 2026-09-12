
namespace Core.Context.Compact;

[Register(typeof(IReactiveCompactService), ServiceLifetime.Singleton)]
public sealed partial class ReactiveCompactService : ServiceEntity, IReactiveCompactService
{
    private const string PromptTooLongPrefix = "prompt_too_long";
    private const string PromptTooLongErrorPrefix = "API Error: prompt_too_long";

    private readonly IMicrocompactService _microcompactService;
    private readonly IMessageGroupingService _groupingService;

    public ReactiveCompactService(
        IMicrocompactService microcompactService,
        IMessageGroupingService? groupingService = null)
    {
        _microcompactService = microcompactService;
        _groupingService = groupingService ?? new MessageGroupingService();
    }

    public Task<CompactResult> RunReactiveCompactAsync(
        IReadOnlyList<ApiMessage> messages,
        string errorMessage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(messages);

        if (!IsPromptTooLongError(errorMessage))
        {
            return Task.FromResult(new CompactResult
            {
                Compacted = false,
                Level = CompactLevel.None,
                Trigger = CompactTrigger.Reactive,
                PreCompactTokenCount = _microcompactService.EstimateMessageTokens(messages),
                PostCompactTokenCount = _microcompactService.EstimateMessageTokens(messages)
            });
        }

        var tokenGap = GetPromptTooLongTokenGap(errorMessage);
        var groups = _groupingService.GroupMessagesByApiRound(messages);

        if (groups.Count < 2)
        {
            return Task.FromResult(new CompactResult
            {
                Compacted = false,
                Level = CompactLevel.None,
                Trigger = CompactTrigger.Reactive,
                PreCompactTokenCount = _microcompactService.EstimateMessageTokens(messages),
                PostCompactTokenCount = _microcompactService.EstimateMessageTokens(messages),
                ErrorMessage = "消息不足以进行响应式压缩"
            });
        }

        var dropCount = CalculateDropCount(groups, tokenGap);
        dropCount = Math.Min(dropCount, groups.Count - 1);

        if (dropCount < 1)
        {
            return Task.FromResult(new CompactResult
            {
                Compacted = false,
                Level = CompactLevel.None,
                Trigger = CompactTrigger.Reactive,
                PreCompactTokenCount = _microcompactService.EstimateMessageTokens(messages),
                PostCompactTokenCount = _microcompactService.EstimateMessageTokens(messages),
                ErrorMessage = "无法再丢弃更多消息组"
            });
        }

        var droppedGroups = groups.Take(dropCount).ToList();
        var keptGroups = groups.Skip(dropCount).ToList();
        var keptMessages = keptGroups.SelectMany(g => g).ToList();

        var droppedSummary = BuildDroppedGroupsSummary(droppedGroups);

        var preCompactTokens = _microcompactService.EstimateMessageTokens(messages);
        var postCompactTokens = _microcompactService.EstimateMessageTokens(keptMessages);

        return Task.FromResult(new CompactResult
        {
            Compacted = true,
            Level = CompactLevel.ReactiveCompact,
            Trigger = CompactTrigger.Reactive,
            Summary = droppedSummary,
            PreCompactTokenCount = preCompactTokens,
            PostCompactTokenCount = postCompactTokens,
            MessagesRemoved = messages.Count - keptMessages.Count,
            MessagesPreserved = keptMessages.Count,
            Metadata = new Dictionary<string, JsonElement>
            {
                ["droppedGroups"] = JsonElementHelper.FromInt32(dropCount),
                ["totalGroups"] = JsonElementHelper.FromInt32(groups.Count),
                ["tokenGap"] = JsonElementHelper.FromInt32(tokenGap ?? 0),
                ["hasSummary"] = JsonElementHelper.FromBoolean(!string.IsNullOrEmpty(droppedSummary))
            }
        });
    }

    public bool IsPromptTooLongError(string errorMessage)
    {
        return !string.IsNullOrEmpty(errorMessage)
            && (errorMessage.Contains(PromptTooLongPrefix, StringComparison.OrdinalIgnoreCase)
                || errorMessage.StartsWith(PromptTooLongErrorPrefix, StringComparison.OrdinalIgnoreCase));
    }

    public int? GetPromptTooLongTokenGap(string errorMessage)
    {
        if (string.IsNullOrEmpty(errorMessage))
        {
            return null;
        }

        var match = Regex.Match(errorMessage, @"(\d+)\s*tokens?\s*(?:over|above|exceeding)", RegexOptions.IgnoreCase);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var gap))
        {
            return gap;
        }

        return null;
    }

    /// <summary>
    /// 为被丢弃的消息组构建结构化占位摘要 — 不调 LLM，提取关键信息避免历史完全丢失
    /// </summary>
    private static string BuildDroppedGroupsSummary(IReadOnlyList<IReadOnlyList<ApiMessage>> droppedGroups)
    {
        if (droppedGroups.Count == 0)
            return string.Empty;

        var sb = new StringBuilder();
        sb.AppendLine("[响应式压缩：以下为被丢弃消息组的关键信息摘要]");
        sb.AppendLine();

        var userMessages = new List<string>();
        var assistantSnippets = new List<string>();
        var toolCallCount = 0;

        foreach (var group in droppedGroups)
        {
            foreach (var msg in group)
            {
                if (msg.Role == MessageRole.User && !string.IsNullOrEmpty(msg.Content))
                {
                    var content = msg.Content.Trim();
                    if (content.Length > 200)
                        content = content[..200] + "...";
                    userMessages.Add(content);
                }
                else if (msg.Role == MessageRole.Assistant && !string.IsNullOrEmpty(msg.Content))
                {
                    var content = msg.Content.Trim();
                    if (content.Length > 150)
                        content = content[..150] + "...";
                    if (!string.IsNullOrWhiteSpace(content))
                        assistantSnippets.Add(content);
                }
                else if (msg.Role == MessageRole.Tool)
                {
                    toolCallCount++;
                }
            }
        }

        if (userMessages.Count > 0)
        {
            sb.AppendLine("用户消息：");
            foreach (var um in userMessages)
                sb.AppendLine($"  - {um}");
            sb.AppendLine();
        }

        if (assistantSnippets.Count > 0)
        {
            sb.AppendLine("助手回复摘要：");
            foreach (var snip in assistantSnippets.Take(10))
                sb.AppendLine($"  - {snip}");
            if (assistantSnippets.Count > 10)
                sb.AppendLine($"  - ...（共 {assistantSnippets.Count} 条，仅显示前10条）");
            sb.AppendLine();
        }

        if (toolCallCount > 0)
        {
            sb.AppendLine($"工具调用次数：{toolCallCount}");
        }

        sb.AppendLine($"被丢弃消息组数：{droppedGroups.Count}");
        return sb.ToString();
    }

    private int CalculateDropCount(IReadOnlyList<IReadOnlyList<ApiMessage>> groups, int? tokenGap)
    {
        if (tokenGap is not null)
        {
            var acc = 0;
            var dropCount = 0;
            foreach (var group in groups)
            {
                acc += _microcompactService.EstimateMessageTokens(group);
                dropCount++;
                if (acc >= tokenGap.Value)
                {
                    break;
                }
            }

            return dropCount;
        }

        return Math.Max(1, (int)Math.Floor(groups.Count * 0.2));
    }
}
