namespace JoinCode.Reasoning.Compression;

/// <summary>
/// 推理上下文压缩器实现 — 视锥过滤 + IContextCompressor 联动
/// </summary>
public sealed class ReasoningContextCompressor : IReasoningContextCompressor
{
    private readonly IContextCompressor? _contextCompressor;
    private readonly DagNodeSummarizer _dagSummarizer;
    private readonly ILogger<ReasoningContextCompressor> _logger;

    public ReasoningContextCompressor(
        ILogger<ReasoningContextCompressor> logger,
        IContextCompressor? contextCompressor = null,
        DagNodeSummarizer? dagSummarizer = null)
    {
        _logger = logger;
        _contextCompressor = contextCompressor;
        _dagSummarizer = dagSummarizer ?? new DagNodeSummarizer(contextCompressor);
    }

    public async Task<CompressedPrompt> CompressForRoleAsync(
        ReasoningContext context,
        AgentRole role,
        int maxPromptTokens,
        CancellationToken ct = default)
    {
        var coneContext = context.GetConeContextForRole(role);
        var visibleItems = context.GetVisibleItemsForRole(role);
        var visibleEvidence = context.GetVisibleEvidenceForRole(role);

        var userPrompt = BuildUserPrompt(role, visibleItems, visibleEvidence, coneContext);
        var estimatedTokens = PromptBudgetEstimator.Estimate(userPrompt);

        if (estimatedTokens <= maxPromptTokens)
        {
            return new CompressedPrompt
            {
                UserPrompt = userPrompt,
                EstimatedTokens = estimatedTokens,
                Method = string.IsNullOrEmpty(coneContext) ? CompressionMethod.None : CompressionMethod.ConeFiltered,
                OriginalTokenEstimate = estimatedTokens,
            };
        }

        var truncatedPrompt = TruncatePrompt(userPrompt, maxPromptTokens);
        var truncatedTokens = PromptBudgetEstimator.Estimate(truncatedPrompt);

        if (_contextCompressor is not null && _contextCompressor.CanCompress(truncatedPrompt, ContentType.Dialogue))
        {
            try
            {
                var result = await _contextCompressor.CompressAsync(
                    truncatedPrompt, ContentType.Dialogue,
                    CompressionOptions.Aggressive, ct).ConfigureAwait(false);

                if (result.IsSuccess)
                {
                    var compressedTokens = PromptBudgetEstimator.Estimate(result.CompressedContent);
                    _logger.LogInformation("[压缩] {Role} prompt {Original}→{Compressed} tokens, 策略:{Strategy}",
                        role, estimatedTokens, compressedTokens, result.StrategyName);

                    return new CompressedPrompt
                    {
                        UserPrompt = result.CompressedContent,
                        EstimatedTokens = compressedTokens,
                        Method = CompressionMethod.LlmCompressed,
                        OriginalTokenEstimate = estimatedTokens,
                        CompressionSummary = $"策略:{result.StrategyName}, 压缩比:{result.CompressionRatio:F2}",
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[压缩] IContextCompressor 调用失败，降级为截断");
            }
        }

        _logger.LogInformation("[截断] {Role} prompt {Original}→{Truncated} tokens",
            role, estimatedTokens, truncatedTokens);

        return new CompressedPrompt
        {
            UserPrompt = truncatedPrompt,
            EstimatedTokens = truncatedTokens,
            Method = CompressionMethod.Truncated,
            OriginalTokenEstimate = estimatedTokens,
            CompressionSummary = "截断降级",
        };
    }

    public async Task SummarizeResolvedNodesAsync(Dag<ReasoningPayload> dag, int threshold = 30, CancellationToken ct = default)
    {
        await _dagSummarizer.SummarizeResolvedNodesAsync(dag, threshold, ct).ConfigureAwait(false);
    }

    private static string BuildUserPrompt(
        AgentRole role,
        IReadOnlyList<DataItem> visibleItems,
        IReadOnlyList<EvidenceRecord> visibleEvidence,
        string coneContext)
    {
        var sb = new StringBuilder();

        if (!string.IsNullOrEmpty(coneContext))
        {
            sb.AppendLine("[视锥上下文]");
            sb.AppendLine(coneContext);
            sb.AppendLine();
        }

        var relevantItems = role switch
        {
            AgentRole.Prosecutor => visibleItems.Where(x => x.State == DataState.Assumption).ToList(),
            AgentRole.Defender => visibleItems.Where(x => x.State is DataState.Verified or DataState.Assumption).ToList(),
            AgentRole.Judge => visibleItems.Where(x => x.State is DataState.Verified or DataState.Assumption).ToList(),
            _ => visibleItems,
        };

        if (relevantItems.Count > 0)
        {
            sb.AppendLine(role switch
            {
                AgentRole.Prosecutor => "请为以下假定各提出至少一条支持证据：",
                AgentRole.Defender => "请审查以下项目，提出反驳证据和质疑：",
                AgentRole.Judge => "请对以下假定做出裁决：",
                _ => "相关数据项：",
            });

            for (var i = 0; i < relevantItems.Count; i++)
            {
                var item = relevantItems[i];
                sb.AppendLine($"{i + 1}. [{item.State}] {item.Content} (置信度:{item.Confidence}%)");
            }

            sb.AppendLine();
        }

        if (role == AgentRole.Judge && visibleEvidence.Count > 0)
        {
            var prosCount = visibleEvidence.Count(e => e.SubmittedBy == AgentRole.Prosecutor);
            var defCount = visibleEvidence.Count(e => e.SubmittedBy == AgentRole.Defender);
            sb.AppendLine($"当前证据概况：控方{prosCount}条，辩方{defCount}条");
        }

        return sb.ToString();
    }

    private static string TruncatePrompt(string prompt, int maxTokens)
    {
        var maxChars = maxTokens * 3;
        if (prompt.Length <= maxChars) return prompt;

        var truncated = prompt[..maxChars];
        var lastNewline = truncated.LastIndexOf('\n');
        if (lastNewline > maxChars / 2)
        {
            truncated = truncated[..lastNewline];
        }

        return truncated + "\n...[已截断]";
    }
}
