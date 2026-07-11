
namespace Core.Tests.Agents;

public class ContextCompressionAgentTests
{
    private readonly IChatClient _kernel;
    private readonly Mock<IContextHierarchy> _mockContextHierarchy;
    private readonly Mock<IContextCompressor> _mockContextCompressor;
    private readonly ContextCompressionAgent _agent;

    public ContextCompressionAgentTests()
    {
        _kernel = JoinCode.Llm.DependencyInjection.ServiceRegistration.CreateEmptyKernel();

        _mockContextHierarchy = new Mock<IContextHierarchy>();
        _mockContextCompressor = new Mock<IContextCompressor>();

        _agent = new ContextCompressionAgent(
            _kernel,
            JoinCode.Abstractions.Clock.SystemClockService.Instance,
            _mockContextHierarchy.Object,
            _mockContextCompressor.Object);
    }

    [Fact]
    public void Constructor_InitializesProperties()
    {
        Assert.Equal("ContextCompressionAgent", _agent.Name);
        Assert.Equal(BuiltInAgentType.ContextCompression, _agent.AgentType);
        Assert.Contains("压缩", _agent.Description);
        Assert.NotNull(_agent.SystemPrompt);
        Assert.Equal(8000, _agent.DefaultTokenThreshold);
    }

    [Fact]
    public async Task ClearContext_ResetsToInitialState()
    {
        var originalContext = await _agent.GetContextAsync().ConfigureAwait(true);

        await _agent.ClearContextAsync().ConfigureAwait(true);
        var newContext = await _agent.GetContextAsync().ConfigureAwait(true);

        Assert.Equal(originalContext.Messages.Count, newContext.Messages.Count);
        Assert.Equal(originalContext.Messages[0].Content, newContext.Messages[0].Content);
    }

    [Fact]
    public void CompressionRequest_CanBeCreated()
    {
        var request = new CompressionRequest
        {
            TargetLayer = ContextLayerType.Summary,
            CompressionLevel = 3,
            PreserveKeywords = new List<string> { "重要", "关键" },
            MaxOutputTokens = 4000
        };

        Assert.Equal(ContextLayerType.Summary, request.TargetLayer);
        Assert.Equal(3, request.CompressionLevel);
        Assert.Equal(2, request.PreserveKeywords.Count);
        Assert.Equal(4000, request.MaxOutputTokens);
    }

    [Fact]
    public void CompressionRequest_GetTargetCompressionRatio_ReturnsCorrectRatio()
    {
        Assert.Equal(0.8, CompressionRequest.Light().GetTargetCompressionRatio());
        Assert.Equal(0.5, CompressionRequest.Standard().GetTargetCompressionRatio());
        Assert.Equal(0.2, CompressionRequest.Aggressive().GetTargetCompressionRatio());
    }

    [Fact]
    public void CompressionRequest_FactoryMethods_CreateCorrectRequests()
    {
        var light = CompressionRequest.Light();
        var standard = CompressionRequest.Standard();
        var aggressive = CompressionRequest.Aggressive();
        var forCode = CompressionRequest.ForCode();
        var forDialogue = CompressionRequest.ForDialogue();

        Assert.Equal(1, light.CompressionLevel);
        Assert.Equal(3, standard.CompressionLevel);
        Assert.Equal(5, aggressive.CompressionLevel);
        Assert.Equal(CompressionContentType.Code, forCode.ContentType);
        Assert.Equal(CompressionContentType.Dialogue, forDialogue.ContentType);
    }

    [Fact]
    public void CompressionReport_CanBeCreated()
    {
        var report = CompressionReport.Create(
            new CompressionReportOptions(
                10000,
                5000,
                new List<string> { "保留信息1", "保留信息2" },
                new List<string> { "丢失信息1" },
                CompressionRequest.Standard(),
                "TestStrategy",
                100));

        Assert.NotNull(report.ReportId);
        Assert.Equal(10000, report.OriginalTokenCount);
        Assert.Equal(5000, report.CompressedTokenCount);
        Assert.Equal(0.5, report.CompressionRatio);
        Assert.Equal(2, report.PreservedInfo.Count);
        Assert.Single(report.LostInfo);
        Assert.Equal(5000, report.SavedTokens);
        Assert.Equal(50, report.CompressionEfficiency);
        Assert.True(report.IsSuccess);
    }

    [Fact]
    public void CompressionReport_CreateFailed_ReturnsFailedReport()
    {
        var report = CompressionReport.CreateFailed(
            10000,
            "压缩失败",
            CompressionRequest.Standard());

        Assert.False(report.IsSuccess);
        Assert.Equal("压缩失败", report.ErrorMessage);
        Assert.Equal(1.0, report.CompressionRatio);
    }

    [Fact]
    public async Task ShouldCompressContext_WithDefaultThreshold_ReturnsCorrectResult()
    {
        _mockContextHierarchy.Setup(h => h.GetTotalTokenCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(9000);

        var result = await _agent.ShouldCompressContextAsync().ConfigureAwait(true);

        Assert.True(result);
    }

    [Fact]
    public async Task ShouldCompressContext_WhenAboveThreshold_ReturnsTrue()
    {
        _mockContextHierarchy.Setup(h => h.GetTotalTokenCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(10000);
        _agent.DefaultTokenThreshold = 8000;

        var result = await _agent.ShouldCompressContextAsync().ConfigureAwait(true);

        Assert.True(result);
    }

    [Fact]
    public async Task ShouldCompressContext_WhenBelowThreshold_ReturnsFalse()
    {
        _mockContextHierarchy.Setup(h => h.GetTotalTokenCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(5000);
        _agent.DefaultTokenThreshold = 8000;

        var result = await _agent.ShouldCompressContextAsync().ConfigureAwait(true);

        Assert.False(result);
    }

    [Fact]
    public async Task ClearHistory_RemovesAllHistory()
    {
        await _agent.ClearHistoryAsync().ConfigureAwait(true);
        var history = await _agent.GetCompressionHistoryAsync().ConfigureAwait(true);

        Assert.Empty(history);
    }

    [Fact]
    public async Task GetCompressionReport_WithInvalidId_ReturnsNull()
    {
        var result = await _agent.GetCompressionReportAsync("invalid-id").ConfigureAwait(true);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetRecentReports_ReturnsCorrectCount()
    {
        var reports = await _agent.GetRecentReportsAsync(5).ConfigureAwait(true);

        Assert.NotNull(reports);
        Assert.Empty(reports);
    }

    [Fact]
    public async Task CompressContextAsync_WithNonExistentLayer_ReturnsFailedReport()
    {
        _mockContextHierarchy.Setup(h => h.GetLayerAsync(It.IsAny<ContextLayerType>(), It.IsAny<CancellationToken>())).ReturnsAsync((IContextLayer?)null);

        var request = CompressionRequest.Standard();

        var report = await _agent.CompressContextAsync(request).ConfigureAwait(true);

        Assert.False(report.IsSuccess);
        Assert.Contains("不存在", report.ErrorMessage);
    }

    [Fact]
    public async Task CompressContextAsync_WithShortContent_ReturnsSkipReport()
    {
        var mockLayer = new Mock<IContextLayer>();
        mockLayer.Setup(l => l.Content).Returns("短内容");

        _mockContextHierarchy.Setup(h => h.GetLayerAsync(It.IsAny<ContextLayerType>(), It.IsAny<CancellationToken>())).ReturnsAsync(mockLayer.Object);

        var request = CompressionRequest.Standard();

        var report = await _agent.CompressContextAsync(request).ConfigureAwait(true);

        Assert.True(report.IsSuccess);
        Assert.Equal("Skip", report.StrategyName);
    }

    [Fact]
    public async Task SummarizeLayerAsync_WithNonExistentLayer_ReturnsEmptyString()
    {
        _mockContextHierarchy.Setup(h => h.GetLayerAsync(It.IsAny<ContextLayerType>(), It.IsAny<CancellationToken>())).ReturnsAsync((IContextLayer?)null);

        var result = await _agent.SummarizeLayerAsync(ContextLayerType.Summary).ConfigureAwait(true);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task SummarizeLayerAsync_WithEmptyContent_ReturnsEmptyString()
    {
        var mockLayer = new Mock<IContextLayer>();
        mockLayer.Setup(l => l.Content).Returns(string.Empty);

        _mockContextHierarchy.Setup(h => h.GetLayerAsync(It.IsAny<ContextLayerType>(), It.IsAny<CancellationToken>())).ReturnsAsync(mockLayer.Object);

        var result = await _agent.SummarizeLayerAsync(ContextLayerType.Summary).ConfigureAwait(true);

        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public async Task AutoCompressIfNeededAsync_WhenNotNeeded_ReturnsNull()
    {
        _mockContextHierarchy.Setup(h => h.GetTotalTokenCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(5000);

        var result = await _agent.AutoCompressIfNeededAsync().ConfigureAwait(true);

        Assert.Null(result);
    }

    [Fact]
    public async Task AnalyzeTokenUsage_ReturnsValidAnalysis()
    {
        var mockLayers = new List<IContextLayer>();
        _mockContextHierarchy.Setup(h => h.GetLayersAsync(It.IsAny<CancellationToken>())).ReturnsAsync(mockLayers);
        _mockContextHierarchy.Setup(h => h.GetTotalTokenCountAsync(It.IsAny<CancellationToken>())).ReturnsAsync(10000);

        var analysis = await _agent.AnalyzeTokenUsageAsync().ConfigureAwait(true);

        Assert.NotNull(analysis);
        Assert.Equal(10000, analysis.TotalTokenCount);
        Assert.Equal(8000, analysis.TokenThreshold);
        Assert.True(analysis.ShouldCompress);
        Assert.NotNull(analysis.LayerDetails);
        Assert.NotNull(analysis.Recommendations);
    }

    [Fact]
    public void TokenUsageAnalysis_Properties_WorkCorrectly()
    {
        var analysis = new TokenUsageAnalysis
        {
            Timestamp = DateTime.UtcNow,
            TotalTokenCount = 10000,
            TokenThreshold = 8000,
            ShouldCompress = true,
            LayerDetails = new List<LayerTokenInfo>
            {
                new()
                {
                    LayerType = ContextLayerType.Detailed,
                    TokenCount = 6000
                }
            },
            Recommendations = new List<string> { "建议1", "建议2" }
        };

        Assert.Equal(10000, analysis.TotalTokenCount);
        Assert.True(analysis.ShouldCompress);
        Assert.Single(analysis.LayerDetails);
        Assert.Equal(2, analysis.Recommendations.Count);
    }

    [Fact]
    public void LayerTokenInfo_Properties_WorkCorrectly()
    {
        var info = new LayerTokenInfo
        {
            LayerType = ContextLayerType.Summary,
            TokenCount = 3000
        };

        Assert.Equal(ContextLayerType.Summary, info.LayerType);
        Assert.Equal(3000, info.TokenCount);
    }

    [Fact]
    public void CompressionRequest_ToCompressionOptions_ReturnsValidOptions()
    {
        var request = new CompressionRequest
        {
            CompressionLevel = 3,
            MaxOutputTokens = 4000,
            PreserveSignatures = true,
            PreserveImports = false,
            UseSummarization = true
        };

        var options = request.ToCompressionOptions();

        Assert.NotNull(options);
        Assert.Equal(0.5, options.TargetCompressionRatio);
        Assert.Equal(4000, options.MaxOutputTokens);
        Assert.True(options.PreserveSignatures);
        Assert.False(options.PreserveImports);
        Assert.True(options.UseSummarization);
    }

    [Theory]
    [InlineData(1, 0.8)]
    [InlineData(2, 0.6)]
    [InlineData(3, 0.5)]
    [InlineData(4, 0.35)]
    [InlineData(5, 0.2)]
    public void CompressionRequest_GetTargetCompressionRatio_ReturnsExpectedValue(int level, double expected)
    {
        var request = new CompressionRequest { CompressionLevel = level };

        var ratio = request.GetTargetCompressionRatio();

        Assert.Equal(expected, ratio);
    }
}
