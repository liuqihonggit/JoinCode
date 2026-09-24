namespace Core.Query.Snip;

/// <summary>
/// 历史裁剪服务接口 — 按 Token 上限或消息数量裁剪对话历史
/// </summary>
public interface IHistorySnipService {
    /// <summary>
    /// 按选项裁剪对话历史
    /// </summary>
    /// <param name="history">对话历史</param>
    /// <param name="options">裁剪选项</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>裁剪结果</returns>
    Task<SnipResult> SnipHistoryAsync(MessageList history, SnipOptions options, CancellationToken ct = default);

    /// <summary>
    /// 按 Token 上限裁剪对话历史
    /// </summary>
    /// <param name="history">对话历史</param>
    /// <param name="maxTokens">Token 上限</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>裁剪结果</returns>
    Task<SnipResult> SnipByTokenLimitAsync(MessageList history, int maxTokens, CancellationToken ct = default);

    /// <summary>
    /// 按消息数量裁剪对话历史
    /// </summary>
    /// <param name="history">对话历史</param>
    /// <param name="maxMessages">消息数量上限</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>裁剪结果</returns>
    Task<SnipResult> SnipByMessageCountAsync(MessageList history, int maxMessages, CancellationToken ct = default);
}

/// <summary>
/// 历史裁剪选项
/// </summary>
public sealed class SnipOptions {
    /// <summary>
    /// Token 上限（可选）
    /// </summary>
    public int? MaxTokens { get; set; }

    /// <summary>
    /// 消息数量上限（可选）
    /// </summary>
    public int? MaxMessages { get; set; }

    /// <summary>
    /// 是否保留系统消息
    /// </summary>
    public bool PreserveSystemMessages { get; set; } = true;

    /// <summary>
    /// 是否保留最近消息
    /// </summary>
    public bool PreserveRecentMessages { get; set; } = true;

    /// <summary>
    /// 保留最近消息的数量
    /// </summary>
    public int RecentMessageCount { get; set; } = 5;

    /// <summary>
    /// 裁剪策略
    /// </summary>
    public SnipStrategy Strategy { get; set; } = SnipStrategy.OldestFirst;
}

/// <summary>
/// 历史裁剪策略
/// </summary>
public enum SnipStrategy {
    /// <summary>
    /// 优先裁剪最旧消息
    /// </summary>
    [EnumValue("oldestFirst")] OldestFirst,

    /// <summary>
    /// 优先裁剪最大消息
    /// </summary>
    [EnumValue("largestFirst")] LargestFirst,

    /// <summary>
    /// 优先裁剪最不相关消息
    /// </summary>
    [EnumValue("leastRelevant")] LeastRelevant
}

/// <summary>
/// 历史裁剪结果
/// </summary>
public sealed class SnipResult {
    /// <summary>
    /// 移除的消息数量
    /// </summary>
    public int MessagesRemoved { get; init; }

    /// <summary>
    /// 移除的 Token 数
    /// </summary>
    public int TokensRemoved { get; init; }

    /// <summary>
    /// 剩余消息数量
    /// </summary>
    public int RemainingMessages { get; init; }

    /// <summary>
    /// 剩余 Token 数
    /// </summary>
    public int RemainingTokens { get; init; }
}

/// <summary>
/// 历史裁剪服务实现 — 按策略选择可移除消息并应用裁剪
/// </summary>
[Register(typeof(IHistorySnipService), ServiceLifetime.Singleton)]
public sealed partial class HistorySnipService : ServiceEntity, IHistorySnipService {
    private const int EstimatedCharsPerToken = 4;
    private readonly ITelemetryService? _telemetryService;

    /// <summary>
    /// 构造函数 — 注入遥测服务（可选）
    /// </summary>
    /// <param name="telemetryService">遥测服务</param>
    public HistorySnipService(ITelemetryService? telemetryService = null) {
        _telemetryService = telemetryService;
    }

    /// <summary>
    /// 按选项裁剪对话历史
    /// </summary>
    /// <param name="history">对话历史</param>
    /// <param name="options">裁剪选项</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>裁剪结果</returns>
    public Task<SnipResult> SnipHistoryAsync(MessageList history, SnipOptions options, CancellationToken ct = default) {
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

    /// <summary>
    /// 按 Token 上限裁剪对话历史
    /// </summary>
    /// <param name="history">对话历史</param>
    /// <param name="maxTokens">Token 上限</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>裁剪结果</returns>
    public Task<SnipResult> SnipByTokenLimitAsync(MessageList history, int maxTokens, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(history);

        ct.ThrowIfCancellationRequested();

        var options = new SnipOptions {
            MaxTokens = maxTokens,
            PreserveSystemMessages = true,
            PreserveRecentMessages = true,
            RecentMessageCount = 5,
            Strategy = SnipStrategy.OldestFirst
        };

        return SnipHistoryAsync(history, options, ct);
    }

    /// <summary>
    /// 按消息数量裁剪对话历史
    /// </summary>
    /// <param name="history">对话历史</param>
    /// <param name="maxMessages">消息数量上限</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>裁剪结果</returns>
    public Task<SnipResult> SnipByMessageCountAsync(MessageList history, int maxMessages, CancellationToken ct = default) {
        ArgumentNullException.ThrowIfNull(history);

        ct.ThrowIfCancellationRequested();

        var options = new SnipOptions {
            MaxMessages = maxMessages,
            PreserveSystemMessages = true,
            PreserveRecentMessages = true,
            RecentMessageCount = Math.Min(5, maxMessages),
            Strategy = SnipStrategy.OldestFirst
        };

        return SnipHistoryAsync(history, options, ct);
    }

    private static List<int> GetRemovableIndices(MessageList history, SnipOptions options) {
        var removable = new List<int>();
        var recentStartIndex = Math.Max(0, history.Count - options.RecentMessageCount);

        for (var i = 0; i < history.Count; i++) {
            var message = history[i];

            if (options.PreserveSystemMessages && message.Role == MessageRole.System) {
                continue;
            }

            if (options.PreserveRecentMessages && i >= recentStartIndex) {
                continue;
            }

            removable.Add(i);
        }

        return removable;
    }

    private static List<int> SelectIndicesToRemove(MessageList history, List<int> removableIndices, SnipOptions options) {
        if (removableIndices.Count == 0) {
            return [];
        }

        var totalTokens = EstimateTotalTokens(history);
        var needsTokenSnip = options.MaxTokens.HasValue && totalTokens > options.MaxTokens.Value;
        var needsMessageSnip = options.MaxMessages.HasValue && history.Count > options.MaxMessages.Value;

        if (!needsTokenSnip && !needsMessageSnip) {
            return [];
        }

        var indicesToRemove = options.Strategy switch {
            SnipStrategy.OldestFirst => removableIndices.OrderBy(i => i),
            SnipStrategy.LargestFirst => removableIndices
                .OrderByDescending(i => EstimateMessageTokens(history[i]))
                .ThenBy(i => i),
            SnipStrategy.LeastRelevant => removableIndices
                .OrderBy(i => GetMessageRelevanceScore(history[i]))
                .ThenBy(i => i),
            _ => removableIndices.OrderBy(i => i)
        };

        var result = new List<int>();
        var tokensRemoved = 0;
        var messagesRemoved = 0;

        foreach (var index in indicesToRemove) {
            var wouldExceedMessageLimit = options.MaxMessages.HasValue &&
                (history.Count - messagesRemoved) > options.MaxMessages.Value;

            var wouldExceedTokenLimit = options.MaxTokens.HasValue &&
                (totalTokens - tokensRemoved) > options.MaxTokens.Value;

            if (!wouldExceedMessageLimit && !wouldExceedTokenLimit) {
                break;
            }

            result.Add(index);
            tokensRemoved += EstimateMessageTokens(history[index]);
            messagesRemoved++;
        }

        return result;
    }

    private static SnipResult ApplySnip(MessageList history, List<int> indicesToRemove) {
        if (indicesToRemove.Count == 0) {
            return new SnipResult {
                MessagesRemoved = 0,
                TokensRemoved = 0,
                RemainingMessages = history.Count,
                RemainingTokens = EstimateTotalTokens(history)
            };
        }

        var tokensRemoved = 0;
        foreach (var index in indicesToRemove) {
            tokensRemoved += EstimateMessageTokens(history[index]);
        }

        for (var i = indicesToRemove.Count - 1; i >= 0; i--) {
            history.RemoveAt(indicesToRemove[i]);
        }

        return new SnipResult {
            MessagesRemoved = indicesToRemove.Count,
            TokensRemoved = tokensRemoved,
            RemainingMessages = history.Count,
            RemainingTokens = EstimateTotalTokens(history)
        };
    }

    private static int EstimateMessageTokens(ApiMessage message) {
        if (string.IsNullOrEmpty(message.Content)) {
            return 0;
        }

        return (message.Content.Length + EstimatedCharsPerToken - 1) / EstimatedCharsPerToken;
    }

    private static int EstimateTotalTokens(MessageList history) {
        var total = 0;
        foreach (var message in history) {
            total += EstimateMessageTokens(message);
        }
        return total;
    }

    private static int GetMessageRelevanceScore(ApiMessage message) {
        var score = 0;

        if (message.Role == MessageRole.Assistant) {
            score += 2;
        } else if (message.Role == MessageRole.User) {
            score += 3;
        }

        if (!string.IsNullOrEmpty(message.Content)) {
            score += Math.Min(10, message.Content.Length / 100);
        }

        return score;
    }
}