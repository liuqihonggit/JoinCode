
namespace Core.Agents;

/// <summary>
/// 上下文压缩 Agent - 智能压缩和管理上下文以优化 Token 使用
/// </summary>
public sealed class ContextCompressionAgent : BuiltInAgentBase
{
    private readonly IContextHierarchy _contextHierarchy;
    private readonly IContextCompressor _contextCompressor;
    private readonly List<CompressionReport> _compressionHistory;
    private readonly SemaphoreSlim _historyLock;

    public override string Name => "ContextCompressionAgent";

    public override string Description => "智能压缩和管理上下文，优化 Token 使用并保留关键信息";

    public override BuiltInAgentType AgentType => BuiltInAgentType.ContextCompression;

    public override string SystemPrompt => AgentPrompts.ContextCompressionAgentSystemPrompt;

    /// <summary>
    /// 默认 Token 阈值，超过此值触发自动压缩
    /// </summary>
    public int DefaultTokenThreshold { get; set; } = 8000;

    /// <summary>
    /// 压缩历史记录
    /// </summary>
    public async Task<IReadOnlyList<CompressionReport>> GetCompressionHistoryAsync(CancellationToken ct = default)
    {
        await _historyLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _compressionHistory;
        }
        finally
        {
            _historyLock.Release();
        }
    }

    public ContextCompressionAgent(
        IChatClient kernel,
        IClockService clock,
        IContextHierarchy contextHierarchy,
        IContextCompressor contextCompressor,
        ILogger<ContextCompressionAgent>? logger = null)
        : base(kernel, clock, logger)
    {
        _contextHierarchy = contextHierarchy ?? throw new ArgumentNullException(nameof(contextHierarchy));
        _contextCompressor = contextCompressor ?? throw new ArgumentNullException(nameof(contextCompressor));
        _compressionHistory = new List<CompressionReport>();
        _historyLock = new SemaphoreSlim(1, 1);
    }

    /// <summary>
    /// 压缩指定上下文
    /// </summary>
    /// <param name="request">压缩请求参数</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>压缩报告</returns>
    public async Task<CompressionReport> CompressContextAsync(
        CompressionRequest request,
        CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();

        try
        {
            Logger?.LogInformation(
                "[{AgentName}] 开始压缩上下文，目标层级: {TargetLayer}, 压缩级别: {CompressionLevel}",
                Name, request.TargetLayer, request.CompressionLevel);

            var layer = await _contextHierarchy.GetLayerAsync(request.TargetLayer, cancellationToken).ConfigureAwait(false);
            if (layer == null)
            {
                var errorReport = CompressionReport.CreateFailed(
                    0,
                    $"目标层级 {request.TargetLayer} 不存在",
                    request);

                await AddToHistoryAsync(errorReport, cancellationToken).ConfigureAwait(false);
                return errorReport;
            }

            var originalContent = layer.Content;
            var originalTokenCount = EstimateTokenCount(originalContent);

            if (originalTokenCount < request.MinCompressionThreshold)
            {
                var skipReport = CompressionReport.Create(
                    new CompressionReportOptions(
                        originalTokenCount,
                        originalTokenCount,
                        new List<string> { "内容长度低于压缩阈值" },
                        new List<string>(),
                        request,
                        "Skip",
                        stopwatch.ElapsedMilliseconds));

                await AddToHistoryAsync(skipReport, cancellationToken).ConfigureAwait(false);
                return skipReport;
            }

            var compressionOptions = request.ToCompressionOptions();
            var contentType = GetContentTypeForLayer(request.TargetLayer);

            var result = await _contextCompressor.CompressAsync(
                originalContent,
                contentType,
                compressionOptions,
                cancellationToken).ConfigureAwait(false);

            stopwatch.Stop();

            if (!result.IsSuccess)
            {
                var failedReport = CompressionReport.CreateFailed(
                    originalTokenCount,
                    result.ErrorMessage ?? "压缩失败",
                    request);

                await AddToHistoryAsync(failedReport, cancellationToken).ConfigureAwait(false);
                return failedReport;
            }

            var compressedTokenCount = EstimateTokenCount(result.CompressedContent);
            var preservedInfo = ExtractPreservedInfo(originalContent, result.CompressedContent);
            var lostInfo = ExtractLostInfo(originalContent, result.CompressedContent);

            var report = CompressionReport.Create(
                new CompressionReportOptions(
                    originalTokenCount,
                    compressedTokenCount,
                    preservedInfo,
                    lostInfo,
                    request,
                    result.StrategyName,
                    stopwatch.ElapsedMilliseconds));

            await AddToHistoryAsync(report, cancellationToken).ConfigureAwait(false);

            Logger?.LogInformation(
                "[{AgentName}] 上下文压缩完成，原始 Token: {Original}, 压缩后: {Compressed}, 比率: {Ratio:P}",
                Name, originalTokenCount, compressedTokenCount, report.CompressionRatio);

            return report;
        }
        catch (OperationCanceledException)
        {
            Logger?.LogWarning("[{AgentName}] 上下文压缩已取消", Name);
            throw;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "[{AgentName}] 压缩上下文时出错", Name);

            var errorReport = CompressionReport.CreateFailed(
                0,
                ex.Message,
                request);

            await AddToHistoryAsync(errorReport, cancellationToken).ConfigureAwait(false);
            return errorReport;
        }
    }

    /// <summary>
    /// 为指定层生成摘要
    /// </summary>
    /// <param name="layerType">目标层级</param>
    /// <param name="maxSummaryLength">摘要最大长度</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>生成的摘要</returns>
    public async Task<string> SummarizeLayerAsync(
        ContextLayerType layerType,
        int maxSummaryLength = 500,
        CancellationToken cancellationToken = default)
    {
        try
        {
            Logger?.LogInformation(
                "[{AgentName}] 开始为层级 {LayerType} 生成摘要",
                Name, layerType);

            var layer = await _contextHierarchy.GetLayerAsync(layerType, cancellationToken).ConfigureAwait(false);
            if (layer == null)
            {
                Logger?.LogWarning("[{AgentName}] 层级 {LayerType} 不存在", Name, layerType);
                return string.Empty;
            }

            var content = layer.Content;
            if (string.IsNullOrWhiteSpace(content))
            {
                return string.Empty;
            }

            var request = new CompressionRequest
            {
                TargetLayer = layerType,
                CompressionLevel = 3,
                MaxOutputTokens = maxSummaryLength,
                UseSummarization = true,
                ContentType = GetContentTypeForLayer(layerType)
            };

            var options = request.ToCompressionOptions();
            var contentType = GetContentTypeForLayer(layerType);

            var result = await _contextCompressor.CompressAsync(
                content,
                contentType,
                options,
                cancellationToken).ConfigureAwait(false);

            if (!result.IsSuccess)
            {
                Logger?.LogWarning(
                    "[{AgentName}] 生成摘要失败: {Error}",
                    Name, result.ErrorMessage);
                return string.Empty;
            }

            Logger?.LogInformation(
                "[{AgentName}] 摘要生成完成，长度: {Length}",
                Name, result.CompressedContent.Length);

            return result.CompressedContent;
        }
        catch (OperationCanceledException)
        {
            Logger?.LogWarning("[{AgentName}] 摘要生成已取消", Name);
            throw;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "[{AgentName}] 生成摘要时出错", Name);
            return string.Empty;
        }
    }

    /// <summary>
    /// 分析 Token 使用情况
    /// </summary>
    /// <returns>Token 使用分析报告</returns>
    public async Task<TokenUsageAnalysis> AnalyzeTokenUsageAsync(CancellationToken cancellationToken = default)
    {
        var totalTokens = await _contextHierarchy.GetTotalTokenCountAsync(cancellationToken).ConfigureAwait(false);
        var analysis = new TokenUsageAnalysis
        {
            Timestamp = _clock.GetUtcNow(),
            TotalTokenCount = totalTokens,
            TokenThreshold = DefaultTokenThreshold,
            ShouldCompress = totalTokens > DefaultTokenThreshold
        };

        var layers = await _contextHierarchy.GetLayersAsync(cancellationToken).ConfigureAwait(false);
        analysis.LayerDetails.AddRange(
            layers.Select(layer => new LayerTokenInfo
            {
                LayerType = layer.LayerType,
                TokenCount = EstimateTokenCount(layer.Content)
            }));

        var recommendations = GenerateRecommendations(analysis);
        analysis = analysis with { Recommendations = recommendations };

        Logger?.LogInformation(
            "[{AgentName}] Token 使用分析完成，总计: {Total}, 建议压缩: {ShouldCompress}",
            Name, analysis.TotalTokenCount, analysis.ShouldCompress);

        return analysis;
    }

    /// <summary>
    /// 异步获取压缩报告
    /// </summary>
    /// <param name="reportId">报告ID</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>压缩报告，如果不存在则返回 null</returns>
    public async Task<CompressionReport?> GetCompressionReportAsync(string reportId, CancellationToken ct = default)
    {
        await _historyLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _compressionHistory.FirstOrDefault(r => r.ReportId == reportId);
        }
        finally
        {
            _historyLock.Release();
        }
    }

    /// <summary>
    /// 异步获取最近的压缩报告
    /// </summary>
    /// <param name="count">报告数量</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>压缩报告列表</returns>
    public async Task<IReadOnlyList<CompressionReport>> GetRecentReportsAsync(int count = 10, CancellationToken ct = default)
    {
        await _historyLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            return _compressionHistory
                .OrderByDescending(r => r.Timestamp)
                .Take(count)
                .ToList();
        }
        finally
        {
            _historyLock.Release();
        }
    }

    /// <summary>
    /// 异步清除压缩历史
    /// </summary>
    /// <param name="ct">取消令牌</param>
    public async Task ClearHistoryAsync(CancellationToken ct = default)
    {
        await _historyLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _compressionHistory.Clear();
            Logger?.LogInformation("[{AgentName}] 压缩历史已清除", Name);
        }
        finally
        {
            _historyLock.Release();
        }
    }

    /// <summary>
    /// 检查是否应该压缩上下文
    /// </summary>
    /// <returns>是否应该压缩</returns>
    public async Task<bool> ShouldCompressContextAsync(CancellationToken cancellationToken = default)
    {
        var totalTokens = await _contextHierarchy.GetTotalTokenCountAsync(cancellationToken).ConfigureAwait(false);
        return totalTokens > DefaultTokenThreshold;
    }

    /// <summary>
    /// 自动压缩上下文（如果超过阈值）
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>压缩报告，如果未触发压缩则返回 null</returns>
    public async Task<CompressionReport?> AutoCompressIfNeededAsync(
        CancellationToken cancellationToken = default)
    {
        if (!await ShouldCompressContextAsync(cancellationToken).ConfigureAwait(false))
        {
            return null;
        }

        var detailedLayer = await _contextHierarchy.GetLayerAsync(ContextLayerType.Detailed, cancellationToken).ConfigureAwait(false);
        if (detailedLayer == null)
        {
            return null;
        }

        var request = CompressionRequest.Standard(ContextLayerType.Summary);
        return await CompressContextAsync(request, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 异步获取压缩统计信息
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>统计信息字典</returns>
    public async Task<Dictionary<string, JsonElement>> GetStatisticsAsync(CancellationToken ct = default)
    {
        await _historyLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            if (_compressionHistory.Count == 0)
            {
                return new Dictionary<string, JsonElement>
                {
                    ["TotalOperations"] = JsonSerializer.SerializeToElement(0, AgentsJsonContext.Default.Int32),
                    ["AverageCompressionRatio"] = JsonSerializer.SerializeToElement(0.0, AgentsJsonContext.Default.Double),
                    ["TotalTokensSaved"] = JsonSerializer.SerializeToElement(0, AgentsJsonContext.Default.Int32)
                };
            }

            var successfulReports = _compressionHistory.Where(r => r.IsSuccess && r.CompressionRatio > 0).ToList();
            var averageRatio = successfulReports.Any()
                ? successfulReports.Average(r => r.CompressionRatio)
                : 0.0;

            var totalTokensSaved = successfulReports.Sum(r => r.OriginalTokenCount - r.CompressedTokenCount);

            return new Dictionary<string, JsonElement>
            {
                ["TotalOperations"] = JsonSerializer.SerializeToElement(_compressionHistory.Count, AgentsJsonContext.Default.Int32),
                ["SuccessfulOperations"] = JsonSerializer.SerializeToElement(successfulReports.Count, AgentsJsonContext.Default.Int32),
                ["FailedOperations"] = JsonSerializer.SerializeToElement(_compressionHistory.Count(r => !r.IsSuccess), AgentsJsonContext.Default.Int32),
                ["AverageCompressionRatio"] = JsonSerializer.SerializeToElement(averageRatio, AgentsJsonContext.Default.Double),
                ["TotalTokensSaved"] = JsonSerializer.SerializeToElement(totalTokensSaved, AgentsJsonContext.Default.Int32),
                ["LastOperationTime"] = JsonSerializer.SerializeToElement(_compressionHistory.LastOrDefault()?.Timestamp ?? DateTime.MinValue, AgentsJsonContext.Default.String)
            };
        }
        finally
        {
            _historyLock.Release();
        }
    }

    private async Task AddToHistoryAsync(CompressionReport report, CancellationToken ct = default)
    {
        await _historyLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            _compressionHistory.Add(report);

            // 限制历史记录数量
            const int maxHistorySize = WorkflowConstants.ContextCompression.MaxHistorySize;
            if (_compressionHistory.Count > maxHistorySize)
            {
                _compressionHistory.RemoveAt(0);
            }
        }
        finally
        {
            _historyLock.Release();
        }
    }

    private List<string> GenerateRecommendations(TokenUsageAnalysis analysis)
    {
        var recommendations = new List<string>();

        if (analysis.TotalTokenCount > analysis.TokenThreshold * 1.5)
        {
            recommendations.Add("Token 使用量远超阈值，建议立即进行激进压缩");
        }
        else if (analysis.TotalTokenCount > analysis.TokenThreshold)
        {
            recommendations.Add("Token 使用量超过阈值，建议进行标准压缩");
        }

        var detailedLayer = analysis.LayerDetails.FirstOrDefault(l => l.LayerType == ContextLayerType.Detailed);
        if (detailedLayer != null && detailedLayer.TokenCount > analysis.TokenThreshold * 0.5)
        {
            recommendations.Add("详细层占用大量 Token，建议优先压缩");
        }

        if (!recommendations.Any())
        {
            recommendations.Add("Token 使用正常，无需压缩");
        }

        return recommendations;
    }

    private int EstimateTokenCount(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return 0;
        }

        // 简单估算：每 4 个字符约等于 1 个 token
        return content.Length / 4;
    }

    private CompressionContentType GetContentTypeForLayer(ContextLayerType layerType)
    {
        return layerType switch
        {
            ContextLayerType.Detailed => CompressionContentType.Code,
            ContextLayerType.Summary => CompressionContentType.Dialogue,
            ContextLayerType.Index => CompressionContentType.ReferenceIndex,
            _ => CompressionContentType.Text
        };
    }

    private List<string> ExtractPreservedInfo(string original, string compressed)
    {
        var preserved = new List<string>();

        // 提取保留的关键信息（简化实现）
        if (compressed.Contains("function") || compressed.Contains("class"))
            preserved.Add("函数/类定义");

        if (compressed.Contains("import") || compressed.Contains("using"))
            preserved.Add("导入语句");

        if (compressed.Length > 0)
            preserved.Add("核心逻辑");

        return preserved;
    }

    private List<string> ExtractLostInfo(string original, string compressed)
    {
        var lost = new List<string>();

        // 估算丢失的信息（简化实现）
        var originalLines = original.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;
        var compressedLines = compressed.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;

        if (compressedLines < originalLines * 0.5)
        {
            lost.Add("部分详细实现");
        }

        if (compressed.Length < original.Length * 0.3)
        {
            lost.Add("大量细节内容");
        }

        if (!lost.Any())
        {
            lost.Add("少量细节");
        }

        return lost;
    }

    public override async ValueTask DisposeAsync()
    {
        _historyLock.Dispose();
        await base.DisposeAsync().ConfigureAwait(false);
    }
}

/// <summary>
/// Token 使用分析结果
/// </summary>
public sealed record TokenUsageAnalysis
{
    /// <summary>
    /// 分析时间戳
    /// </summary>
    public DateTime Timestamp { get; init; }

    /// <summary>
    /// 总 Token 数量
    /// </summary>
    public int TotalTokenCount { get; init; }

    /// <summary>
    /// Token 阈值
    /// </summary>
    public int TokenThreshold { get; init; }

    /// <summary>
    /// 是否应该压缩
    /// </summary>
    public bool ShouldCompress { get; init; }

    /// <summary>
    /// 各层详细信息
    /// </summary>
    public List<LayerTokenInfo> LayerDetails { get; init; } = new();

    /// <summary>
    /// 压缩建议
    /// </summary>
    public List<string> Recommendations { get; init; } = new();
}

/// <summary>
/// 层级 Token 信息
/// </summary>
public sealed record LayerTokenInfo
{
    /// <summary>
    /// 层级类型
    /// </summary>
    public required ContextLayerType LayerType { get; init; }

    /// <summary>
    /// Token 数量
    /// </summary>
    public required int TokenCount { get; init; }
}
