
namespace Core.Context.Compression;

/// <summary>
/// 对话历史压缩策略
/// </summary>
[Register(typeof(ICompressionStrategy), ServiceLifetime.Transient)]
public sealed partial class DialogueCompressor : CompressionStrategyBase
{
    public override string Name => "DialogueCompressor";
    public override string Description => "Compresses dialogue history by summarizing older messages while preserving recent context and key decisions";
    public override int Priority => 100;

    private static readonly HashSet<ContentType> _supportedTypes = new()
    {
        ContentType.Dialogue
    };

    private static readonly Regex[] MessagePatterns =
    [
        new(@"(User|Assistant|System|Human|AI|Bot):\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"<(user|assistant|system|human|ai|bot)>", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"\[?(User|Assistant|System|Human|AI|Bot)\]?:?\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\s*(>{1,3})\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    private static readonly Regex[] UserMessagePatterns =
    [
        new(@"^(User|Human):\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^<(user|human)>", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new(@"^\[?(User|Human)\]?:?\s*", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    private static readonly Regex[] DecisionPatterns =
    [
        new(@"(?i)(决定|decided?|decision)\s*[:：]\s*(.+?)(?:\n|$)", RegexOptions.Compiled),
        new(@"(?i)(选择|chose|choice|selected?)\s*[:：]\s*(.+?)(?:\n|$)", RegexOptions.Compiled),
        new(@"(?i)(同意|agreed?|approved?)\s*[:：]\s*(.+?)(?:\n|$)", RegexOptions.Compiled),
        new(@"(?i)(确认|confirmed?)\s*[:：]\s*(.+?)(?:\n|$)", RegexOptions.Compiled),
        new(@"(?i)(结论|conclusion)\s*[:：]\s*(.+?)(?:\n|$)", RegexOptions.Compiled),
        new(@"(?i)(方案|solution|plan)\s*[:：]\s*(.+?)(?:\n|$)", RegexOptions.Compiled),
    ];

    private static readonly Regex[] TopicPatterns =
    [
        new(@"(?i)(关于|regarding|about|topic|主题)\s*[:：]\s*(.+?)(?:\n|$)", RegexOptions.Compiled),
        new(@"(?i)(讨论|discussing?)\s*(.+?)(?:\n|$)", RegexOptions.Compiled),
        new(@"(?i)(问题|issue|problem|question)\s*[:：]\s*(.+?)(?:\n|$)", RegexOptions.Compiled),
    ];

    private static readonly Regex[] ActionPatterns =
    [
        new(@"(?i)(创建|created?|create)\s+(.+?)(?:\n|$)", RegexOptions.Compiled),
        new(@"(?i)(修改|modified?|updated?|changed?)\s+(.+?)(?:\n|$)", RegexOptions.Compiled),
        new(@"(?i)(删除|deleted?|removed?)\s+(.+?)(?:\n|$)", RegexOptions.Compiled),
        new(@"(?i)(添加|added?)\s+(.+?)(?:\n|$)", RegexOptions.Compiled),
        new(@"(?i)(实现|implemented?)\s+(.+?)(?:\n|$)", RegexOptions.Compiled),
        new(@"(?i)(修复|fixed?)\s+(.+?)(?:\n|$)", RegexOptions.Compiled),
    ];

    public override IReadOnlySet<ContentType> SupportedContentTypes => _supportedTypes;

    public override Task<string> CompressAsync(
        string content,
        CompressionOptions options,
        CancellationToken cancellationToken = default)
    {
        ValidateOptions(options);

        if (string.IsNullOrWhiteSpace(content))
        {
            return Task.FromResult(content);
        }

        var messages = ParseMessages(content);

        var rounds = GroupIntoRounds(messages);
        if (rounds.Count <= options.DialogueRoundsToPreserve)
        {
            return Task.FromResult(content);
        }

        cancellationToken.ThrowIfCancellationRequested();

        var result = new StringBuilder();

        var roundsToSummarize = rounds.Take(rounds.Count - options.DialogueRoundsToPreserve).ToList();
        var roundsToPreserve = rounds.Skip(rounds.Count - options.DialogueRoundsToPreserve).ToList();
        var messagesToSummarize = roundsToSummarize.SelectMany(r => r.Messages).ToList();
        var messagesToPreserve = roundsToPreserve.SelectMany(r => r.Messages).ToList();

        cancellationToken.ThrowIfCancellationRequested();

        if (options.UseSummarization && messagesToSummarize.Count > 0)
        {
            var summary = GenerateSummary(messagesToSummarize, options);
            cancellationToken.ThrowIfCancellationRequested();
            if (!string.IsNullOrWhiteSpace(summary))
            {
                result.AppendLine("[对话摘要 - 早期内容]");
                result.AppendLine(summary);
                result.AppendLine();
            }
        }

        if (options.PreserveKeyDecisions)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var keyDecisions = ExtractKeyDecisions(messagesToSummarize);
            if (keyDecisions.Count > 0)
            {
                result.AppendLine("[关键决策点]");
                foreach (var decision in keyDecisions)
                {
                    result.AppendLine($"- {decision}");
                }
                result.AppendLine();
            }
        }

        result.AppendLine("[最近对话]");
        foreach (var message in messagesToPreserve)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.AppendLine(message);
            result.AppendLine();
        }

        // 使用 Span 优化 TrimEnd
        var resultSpan = result.ToString().AsSpan().TrimEnd();
        return Task.FromResult(resultSpan.ToString());
    }

    public override double EstimateCompressionRatio(string content, CompressionOptions options)
    {
        if (string.IsNullOrWhiteSpace(content))
            return 1.0;

        var messages = ParseMessages(content);
        var rounds = GroupIntoRounds(messages);

        // 使用轮次数量而不是消息数量
        if (rounds.Count <= options.DialogueRoundsToPreserve)
            return 1.0;

        var roundsToSummarize = rounds.Count - options.DialogueRoundsToPreserve;
        var estimatedSummaryLength = options.UseSummarization
            ? options.MaxSummaryLength
            : roundsToSummarize * 100;

        var keyDecisionsLength = options.PreserveKeyDecisions
            ? roundsToSummarize * 30
            : 0;

        var preservedLength = options.DialogueRoundsToPreserve * (content.Length / Math.Max(rounds.Count, 1));

        var estimatedCompressedLength = estimatedSummaryLength + keyDecisionsLength + preservedLength;

        return Math.Min((double)estimatedCompressedLength / content.Length, 1.0);
    }

    private static List<string> ParseMessages(string content)
    {
        var messages = new List<string>();

        var currentMessage = new StringBuilder();
        var lines = content.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);

        foreach (var line in lines)
        {
            var isNewMessage = false;
            foreach (var regex in MessagePatterns)
            {
                if (regex.IsMatch(line))
                {
                    isNewMessage = true;
                    break;
                }
            }

            if (isNewMessage && currentMessage.Length > 0)
            {
                messages.Add(currentMessage.ToString().AsSpan().Trim().ToString());
                currentMessage.Clear();
            }

            currentMessage.AppendLine(line);
        }

        if (currentMessage.Length > 0)
        {
            messages.Add(currentMessage.ToString().AsSpan().Trim().ToString());
        }

        if (messages.Count == 0 && !string.IsNullOrWhiteSpace(content))
        {
            messages.Add(content.Trim());
        }

        return messages;
    }

    private static List<DialogueRound> GroupIntoRounds(List<string> messages)
    {
        var rounds = new List<DialogueRound>();

        DialogueRound? currentRound = null;
        foreach (var message in messages)
        {
            var isUserMessage = false;
            foreach (var regex in UserMessagePatterns)
            {
                if (regex.IsMatch(message))
                {
                    isUserMessage = true;
                    break;
                }
            }

            if (isUserMessage)
            {
                if (currentRound != null)
                {
                    rounds.Add(currentRound);
                }
                currentRound = new DialogueRound();
            }

            currentRound?.Messages.Add(message);
        }

        if (currentRound != null)
        {
            rounds.Add(currentRound);
        }

        // 如果没有识别出任何轮次，将每条消息作为一个独立轮次
        if (rounds.Count == 0 && messages.Count > 0)
        {
            rounds = messages.Select(m => new DialogueRound { Messages = { m } }).ToList();
        }

        return rounds;
    }

    private sealed class DialogueRound
    {
        public List<string> Messages { get; } = new();
    }

    private static string GenerateSummary(List<string> messages, CompressionOptions options)
    {
        var summary = new StringBuilder();
        var topics = ExtractTopics(messages);
        var actions = ExtractActions(messages);

        if (topics.Count > 0)
        {
            summary.AppendLine($"主题: {string.Join(", ", topics.Take(3))}");
        }

        if (actions.Count > 0)
        {
            summary.AppendLine($"主要操作: {string.Join(", ", actions.Take(3))}");
        }

        var totalExchanges = messages.Count;
        summary.AppendLine($"共 {totalExchanges} 轮对话");

        var resultSpan = summary.ToString().AsSpan().TrimEnd();
        if (resultSpan.Length > options.MaxSummaryLength)
        {
            return string.Concat(resultSpan[..(options.MaxSummaryLength - 3)].ToString(), "...");
        }

        return resultSpan.ToString();
    }

    private static List<string> ExtractKeyDecisions(List<string> messages)
    {
        var decisions = new List<string>();

        foreach (var message in messages)
        {
            foreach (var regex in DecisionPatterns)
            {
                var matches = regex.Matches(message);
                foreach (Match match in matches)
                {
                    if (match.Groups.Count > 1)
                    {
                        var decision = match.Groups[match.Groups.Count - 1].Value.Trim();
                        if (!string.IsNullOrWhiteSpace(decision) && decision.Length > 5)
                        {
                            decisions.Add(decision);
                        }
                    }
                }
            }
        }

        return decisions.Distinct().Take(5).ToList();
    }

    private static List<string> ExtractTopics(List<string> messages)
    {
        var topics = new List<string>();

        foreach (var message in messages)
        {
            foreach (var regex in TopicPatterns)
            {
                var match = regex.Match(message);
                if (match.Success && match.Groups.Count > 1)
                {
                    var topic = match.Groups[match.Groups.Count - 1].Value.Trim();
                    if (!string.IsNullOrWhiteSpace(topic) && topic.Length > 3)
                    {
                        topics.Add(topic);
                    }
                }
            }
        }

        return topics.Distinct().ToList();
    }

    private static List<string> ExtractActions(List<string> messages)
    {
        var actions = new List<string>();

        foreach (var message in messages)
        {
            foreach (var regex in ActionPatterns)
            {
                var matches = regex.Matches(message);
                foreach (Match match in matches)
                {
                    if (match.Groups.Count > 1)
                    {
                        var action = match.Groups[1].Value.Trim();
                        if (!string.IsNullOrWhiteSpace(action))
                        {
                            actions.Add(action);
                        }
                    }
                }
            }
        }

        return actions.Distinct().ToList();
    }
}
