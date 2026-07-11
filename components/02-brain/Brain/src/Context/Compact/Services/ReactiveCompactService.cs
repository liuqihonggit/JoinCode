
namespace Core.Context.Compact;

[Register]
public sealed class ReactiveCompactService : IReactiveCompactService
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

        var keptGroups = groups.Skip(dropCount).ToList();
        var keptMessages = keptGroups.SelectMany(g => g).ToList();

        var preCompactTokens = _microcompactService.EstimateMessageTokens(messages);
        var postCompactTokens = _microcompactService.EstimateMessageTokens(keptMessages);

        return Task.FromResult(new CompactResult
        {
            Compacted = true,
            Level = CompactLevel.ReactiveCompact,
            Trigger = CompactTrigger.Reactive,
            PreCompactTokenCount = preCompactTokens,
            PostCompactTokenCount = postCompactTokens,
            MessagesRemoved = messages.Count - keptMessages.Count,
            MessagesPreserved = keptMessages.Count,
            Metadata = new Dictionary<string, JsonElement>
            {
                ["droppedGroups"] = JsonElementHelper.FromInt32(dropCount),
                ["totalGroups"] = JsonElementHelper.FromInt32(groups.Count),
                ["tokenGap"] = JsonElementHelper.FromInt32(tokenGap ?? 0)
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
