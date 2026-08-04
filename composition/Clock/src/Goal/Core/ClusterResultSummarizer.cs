
namespace Core.Goal;

[Register]
public sealed partial class ClusterResultSummarizer : IClusterResultSummarizer
{
    private readonly IChatClient _kernel;
    [Inject] private readonly ILogger<ClusterResultSummarizer>? _logger;

    public ClusterResultSummarizer(IChatClient kernel, ILogger<ClusterResultSummarizer>? logger = null)
    {
        _kernel = kernel;
        _logger = logger;
    }

    public async Task<ClusterSummary> SummarizeAsync(ClusterSummaryContext context, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        var workerSummaries = context.WorkerOutputs
            .Where(w => w.IsSuccess)
            .Select(w => new WorkerSummary
            {
                SubTaskId = w.SubTaskId,
                Title = w.Title,
                Summary = TruncateOutput(w.Output, 200),
                Score = w.GradingScore,
            })
            .ToList();

        var failedWorkers = context.WorkerOutputs.Where(w => !w.IsSuccess).ToList();
        var overallScore = workerSummaries.Count > 0 ? workerSummaries.Average(w => w.Score) : 0.0;

        var summaryText = await GenerateSummaryTextAsync(context, workerSummaries, failedWorkers, ct).ConfigureAwait(false);

        return new ClusterSummary
        {
            Summary = summaryText,
            WorkerSummaries = workerSummaries,
            OverallScore = overallScore,
        };
    }

    private async Task<string> GenerateSummaryTextAsync(
        ClusterSummaryContext context,
        IReadOnlyList<WorkerSummary> workerSummaries,
        IReadOnlyList<WorkerOutput> failedWorkers,
        CancellationToken ct)
    {
        try
        {
            var prompt = BuildSummaryPrompt(context, workerSummaries, failedWorkers);

            var chatHistory = new MessageList();
            chatHistory.AddSystemMessage(prompt);
            chatHistory.AddUserMessage("Generate a concise summary of the cluster execution results.");

            var executionSettings = new ChatOptions
            {
                Temperature = 0.0f,
                MaxTokens = context.MaxSummaryTokens,
            };

            var chatService = _kernel.GetChatCompletionService();
            var results = await chatService.GetApiMessageContentsAsync(chatHistory, executionSettings, _kernel, ct).ConfigureAwait(false);

            return results.Count > 0 ? results[0].Content ?? "No summary generated" : "No summary generated";
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "LLM summary generation failed, using rule-based fallback");
            return BuildRuleBasedSummary(workerSummaries, failedWorkers);
        }
    }

    internal static string BuildRuleBasedSummary(IReadOnlyList<WorkerSummary> workerSummaries, IReadOnlyList<WorkerOutput> failedWorkers)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"集群执行完成: {workerSummaries.Count} 成功, {failedWorkers.Count} 失败");
        sb.AppendLine($"平均评分: {(workerSummaries.Count > 0 ? workerSummaries.Average(w => w.Score) : 0):P0}");

        foreach (var w in workerSummaries)
        {
            sb.AppendLine($"  ✅ {w.Title}: {w.Summary} (评分: {w.Score:P0})");
        }

        foreach (var f in failedWorkers)
        {
            sb.AppendLine($"  ❌ {f.Title}: 执行失败");
        }

        return sb.ToString();
    }

    private static string BuildSummaryPrompt(
        ClusterSummaryContext context,
        IReadOnlyList<WorkerSummary> workerSummaries,
        IReadOnlyList<WorkerOutput> failedWorkers)
    {
        var successList = string.Join("\n", workerSummaries.Select(w => $"- {w.Title}: {w.Summary} (评分: {w.Score:P0})"));
        var failList = failedWorkers.Count > 0
            ? string.Join("\n", failedWorkers.Select(f => $"- {f.Title}: 失败"))
            : "无";

        return $$$"""
            You are a cluster execution summarizer. Generate a concise summary of the parallel execution results.

            OBJECTIVE: {{{context.Objective}}}
            SUCCESSFUL WORKERS ({{{workerSummaries.Count}}}):
            {{{successList}}}

            FAILED WORKERS ({{{failedWorkers.Count}}}):
            {{{failList}}}

            Generate a summary that covers:
            1. Overall completion status
            2. Key achievements per worker
            3. Any failures or issues
            Keep it under {{{context.MaxSummaryTokens}}} tokens.
            """;
    }

    private static string TruncateOutput(string output, int maxLength)
    {
        if (string.IsNullOrEmpty(output))
        {
            return "无输出";
        }

        return output.Length <= maxLength ? output : output[..maxLength] + "...";
    }
}
