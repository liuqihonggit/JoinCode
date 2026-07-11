namespace Core.Query.Snip;

public interface IHistorySnipService
{
    Task<SnipResult> SnipHistoryAsync(MessageList history, SnipOptions options, CancellationToken ct = default);
    Task<SnipResult> SnipByTokenLimitAsync(MessageList history, int maxTokens, CancellationToken ct = default);
    Task<SnipResult> SnipByMessageCountAsync(MessageList history, int maxMessages, CancellationToken ct = default);
}

public sealed class SnipOptions
{
    public int? MaxTokens { get; set; }
    public int? MaxMessages { get; set; }
    public bool PreserveSystemMessages { get; set; } = true;
    public bool PreserveRecentMessages { get; set; } = true;
    public int RecentMessageCount { get; set; } = 5;
    public SnipStrategy Strategy { get; set; } = SnipStrategy.OldestFirst;
}

public enum SnipStrategy
{
    [EnumValue("oldestFirst")] OldestFirst,
    [EnumValue("largestFirst")] LargestFirst,
    [EnumValue("leastRelevant")] LeastRelevant
}

public sealed class SnipResult
{
    public int MessagesRemoved { get; init; }
    public int TokensRemoved { get; init; }
    public int RemainingMessages { get; init; }
    public int RemainingTokens { get; init; }
}

[Register]
public sealed class HistorySnipService : IHistorySnipService
{
    private const int EstimatedCharsPerToken = 4;
    private readonly ITelemetryService? _telemetryService;

    public HistorySnipService(ITelemetryService? telemetryService = null)
    {
        _telemetryService = telemetryService;
    }

    public Task<SnipResult> SnipHistoryAsync(MessageList history, SnipOptions options, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(history);
        ArgumentNullException.ThrowIfNull(options);

        ct.ThrowIfCancellationRequested();

        var removableIndices = GetRemovableIndices(history, options);
        var indicesToRemove = SelectIndicesToRemove(history, removableIndices, options);

        var result = ApplySnip(history, indicesToRemove);
        _telemetryService?.RecordCount("history.snip.count", new() { ["strategy"] = options.Strategy.ToString() }, "count", "History snip operation count");
        _telemetryService?.RecordHistogram("history.snip.tokens.removed", result.TokensRemoved, new() { ["strategy"] = options.Strategy.ToString() }, "tokens", "Tokens removed by snip");
        return Task.FromResult(result);
    }

    public Task<SnipResult> SnipByTokenLimitAsync(MessageList history, int maxTokens, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(history);

        ct.ThrowIfCancellationRequested();

        var options = new SnipOptions
        {
            MaxTokens = maxTokens,
            PreserveSystemMessages = true,
            PreserveRecentMessages = true,
            RecentMessageCount = 5,
            Strategy = SnipStrategy.OldestFirst
        };

        return SnipHistoryAsync(history, options, ct);
    }

    public Task<SnipResult> SnipByMessageCountAsync(MessageList history, int maxMessages, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(history);

        ct.ThrowIfCancellationRequested();

        var options = new SnipOptions
        {
            MaxMessages = maxMessages,
            PreserveSystemMessages = true,
            PreserveRecentMessages = true,
            RecentMessageCount = Math.Min(5, maxMessages),
            Strategy = SnipStrategy.OldestFirst
        };

        return SnipHistoryAsync(history, options, ct);
    }

    private static List<int> GetRemovableIndices(MessageList history, SnipOptions options)
    {
        var removable = new List<int>();
        var recentStartIndex = Math.Max(0, history.Count - options.RecentMessageCount);

        for (var i = 0; i < history.Count; i++)
        {
            var message = history[i];

            if (options.PreserveSystemMessages && message.Role == MessageRole.System)
            {
                continue;
            }

            if (options.PreserveRecentMessages && i >= recentStartIndex)
            {
                continue;
            }

            removable.Add(i);
        }

        return removable;
    }

    private static List<int> SelectIndicesToRemove(MessageList history, List<int> removableIndices, SnipOptions options)
    {
        if (removableIndices.Count == 0)
        {
            return [];
        }

        var totalTokens = EstimateTotalTokens(history);
        var needsTokenSnip = options.MaxTokens.HasValue && totalTokens > options.MaxTokens.Value;
        var needsMessageSnip = options.MaxMessages.HasValue && history.Count > options.MaxMessages.Value;

        if (!needsTokenSnip && !needsMessageSnip)
        {
            return [];
        }

        var indicesToRemove = options.Strategy switch
        {
            SnipStrategy.OldestFirst => removableIndices.OrderBy(i => i).ToList(),
            SnipStrategy.LargestFirst => removableIndices
                .OrderByDescending(i => EstimateMessageTokens(history[i]))
                .ThenBy(i => i)
                .ToList(),
            SnipStrategy.LeastRelevant => removableIndices
                .OrderBy(i => GetMessageRelevanceScore(history[i]))
                .ThenBy(i => i)
                .ToList(),
            _ => removableIndices.OrderBy(i => i).ToList()
        };

        var result = new List<int>();
        var tokensRemoved = 0;
        var messagesRemoved = 0;

        foreach (var index in indicesToRemove)
        {
            var wouldExceedMessageLimit = options.MaxMessages.HasValue &&
                (history.Count - messagesRemoved) > options.MaxMessages.Value;

            var wouldExceedTokenLimit = options.MaxTokens.HasValue &&
                (totalTokens - tokensRemoved) > options.MaxTokens.Value;

            if (!wouldExceedMessageLimit && !wouldExceedTokenLimit)
            {
                break;
            }

            result.Add(index);
            tokensRemoved += EstimateMessageTokens(history[index]);
            messagesRemoved++;
        }

        return result;
    }

    private static SnipResult ApplySnip(MessageList history, List<int> indicesToRemove)
    {
        if (indicesToRemove.Count == 0)
        {
            return new SnipResult
            {
                MessagesRemoved = 0,
                TokensRemoved = 0,
                RemainingMessages = history.Count,
                RemainingTokens = EstimateTotalTokens(history)
            };
        }

        var tokensRemoved = 0;
        foreach (var index in indicesToRemove)
        {
            tokensRemoved += EstimateMessageTokens(history[index]);
        }

        for (var i = indicesToRemove.Count - 1; i >= 0; i--)
        {
            history.RemoveAt(indicesToRemove[i]);
        }

        return new SnipResult
        {
            MessagesRemoved = indicesToRemove.Count,
            TokensRemoved = tokensRemoved,
            RemainingMessages = history.Count,
            RemainingTokens = EstimateTotalTokens(history)
        };
    }

    private static int EstimateMessageTokens(ApiMessage message)
    {
        if (string.IsNullOrEmpty(message.Content))
        {
            return 0;
        }

        return (message.Content.Length + EstimatedCharsPerToken - 1) / EstimatedCharsPerToken;
    }

    private static int EstimateTotalTokens(MessageList history)
    {
        var total = 0;
        foreach (var message in history)
        {
            total += EstimateMessageTokens(message);
        }
        return total;
    }

    private static int GetMessageRelevanceScore(ApiMessage message)
    {
        var score = 0;

        if (message.Role == MessageRole.Assistant)
        {
            score += 2;
        }
        else if (message.Role == MessageRole.User)
        {
            score += 3;
        }

        if (!string.IsNullOrEmpty(message.Content))
        {
            score += Math.Min(10, message.Content.Length / 100);
        }

        return score;
    }
}
