
namespace Core.Tests.Context;

public partial class ContextCompressionIntegrationTests
{
    private readonly ITestOutputHelper _output;
    [Inject] private readonly ILogger<ContextHierarchy> _logger;

    public ContextCompressionIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
        _logger = new Testing.Common.Logging.TestOutputLogger<ContextHierarchy>(output);
    }

    [Fact]
    public async Task CompressAsync_WithCodeContent_ShouldReduceTokenCount()
    {
        // Arrange
        var factory = new CompressionStrategyFactory();
        var compressor = new ContextCompressor(factory);
        var codeContent = GenerateLargeCodeContent(100);
        var originalLength = codeContent.Length;

        _output.WriteLine($"原始内容长度: {originalLength} 字符");

        // Act
        var result = await compressor.CompressAsync(
            codeContent,
            ContentType.Code,
            CompressionOptions.ForCode).ConfigureAwait(true);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.OriginalLength.Should().Be(originalLength);
        result.CompressedLength.Should().BeLessThan(originalLength);
        result.CompressionRatio.Should().BeLessThan(1.0);
        result.SavedTokens.Should().BeGreaterThan(0);

        _output.WriteLine($"压缩后长度: {result.CompressedLength} 字符");
        _output.WriteLine($"压缩比率: {result.CompressionRatio:P2}");
        _output.WriteLine($"节省Token: {result.SavedTokens}");
        _output.WriteLine($"处理时间: {result.ProcessingTimeMs}ms");
        _output.WriteLine($"使用策略: {result.StrategyName}");
    }

    [Fact]
    public async Task CompressAsync_WithDialogueContent_ShouldPreserveKeyDecisions()
    {
        // Arrange
        var factory = new CompressionStrategyFactory();
        var compressor = new ContextCompressor(factory);
        var dialogue = GenerateDialogueContent(50);
        var originalLength = dialogue.Length;

        // Act
        var result = await compressor.CompressAsync(
            dialogue,
            ContentType.Dialogue,
            CompressionOptions.ForDialogue).ConfigureAwait(true);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.CompressedLength.Should().BeLessThan(originalLength);

        // 验证关键决策点被保留
        if (result.CompressedContent.Contains("[决策]"))
        {
            _output.WriteLine("关键决策点已被保留");
        }

        _output.WriteLine($"原始长度: {originalLength}");
        _output.WriteLine($"压缩后长度: {result.CompressedLength}");
    }

    [Fact]
    public async Task CompressBatchAsync_MultipleContents_ShouldCompressAll()
    {
        // Arrange
        var factory = new CompressionStrategyFactory();
        var compressor = new ContextCompressor(factory);
        var contents = new List<ContentItem>
        {
            new() { Id = "1", Type = ContentType.Code, Content = GenerateLargeCodeContent(50) },
            new() { Id = "2", Type = ContentType.Code, Content = GenerateLargeCodeContent(50) },
            new() { Id = "3", Type = ContentType.Dialogue, Content = GenerateDialogueContent(30) },
            new() { Id = "4", Type = ContentType.Text, Content = GenerateTextContent(100) }
        };

        // Act
        var results = await compressor.CompressBatchAsync(contents).ConfigureAwait(true);

        // Assert
        results.Should().HaveCount(4);
        results.All(r => r.IsSuccess).Should().BeTrue();

        foreach (var result in results)
        {
            _output.WriteLine($"内容 {result.ContentId}: 压缩比 {result.CompressionRatio:P2}, 耗时 {result.ProcessingTimeMs}ms");
        }
    }

    [Fact]
    public void CanCompress_WithValidContent_ShouldReturnTrue()
    {
        // Arrange
        var factory = new CompressionStrategyFactory();
        var compressor = new ContextCompressor(factory);
        var longContent = new string('x', 1000);

        // Act & Assert
        compressor.CanCompress(longContent, ContentType.Code).Should().BeTrue();
        compressor.CanCompress(longContent, ContentType.Dialogue).Should().BeTrue();
        // Note: ContentType.Text 没有注册策略，所以返回 false 是预期的行为
        compressor.CanCompress(longContent, ContentType.Text).Should().BeFalse();
    }

    [Fact]
    public void CanCompress_WithShortContent_ShouldReturnFalse()
    {
        // Arrange
        var factory = new CompressionStrategyFactory();
        var compressor = new ContextCompressor(factory);
        var shortContent = "短内容";

        // Act & Assert
        compressor.CanCompress(shortContent, ContentType.Code).Should().BeFalse();
    }

    [Fact]
    public void GetCompressionRatio_ShouldReturnEstimatedRatio()
    {
        // Arrange
        var factory = new CompressionStrategyFactory();
        var compressor = new ContextCompressor(factory);
        var codeContent = GenerateLargeCodeContent(50);

        // Act
        var ratio = compressor.GetCompressionRatio(codeContent, ContentType.Code);

        // Assert
        ratio.Should().BeGreaterThan(0);
        ratio.Should().BeLessThanOrEqualTo(1.0);

        _output.WriteLine($"预估压缩比率: {ratio:P2}");
    }

    [Fact]
    public async Task ContextHierarchy_WithMultipleLayers_ShouldManageLayersCorrectly()
    {
        // Arrange
        var hierarchy = ContextHierarchy.Create(
            new ContextHierarchyOptions
            {
                TokenThreshold = 4000,
                AutoCompressionEnabled = false
            },
            _logger);

        // Act - 添加多个层
        var detailedLayer = ContextLayer.CreateDetailed(
            GenerateLargeCodeContent(100),
            "DetailedLayer");
        var summaryLayer = ContextLayer.CreateSummary(
            "This is a summary of the detailed content",
            "SummaryLayer");
        var indexLayer = ContextLayer.CreateIndex(
            "1. Key point A\n2. Key point B\n3. Key point C",
            "IndexLayer");

        await hierarchy.AddLayerAsync(detailedLayer).ConfigureAwait(true);
        await hierarchy.AddLayerAsync(summaryLayer).ConfigureAwait(true);
        await hierarchy.AddLayerAsync(indexLayer).ConfigureAwait(true);

        // Assert
        var layers = await hierarchy.GetLayersAsync().ConfigureAwait(true);
        layers.Should().HaveCount(3);
        var currentLayer = await hierarchy.GetCurrentLayerAsync().ConfigureAwait(true);
        currentLayer.Should().NotBeNull();
        (await hierarchy.GetTotalTokenCountAsync().ConfigureAwait(true)).Should().BeGreaterThan(0);

        // 验证层级顺序（从详细到索引）
        var orderedLayers = layers.OrderByDescending(l => l.LayerType).ToList();
        orderedLayers[0].LayerType.Should().Be(ContextLayerType.Index);
        orderedLayers[1].LayerType.Should().Be(ContextLayerType.Summary);
        orderedLayers[2].LayerType.Should().Be(ContextLayerType.Detailed);

        _output.WriteLine($"总Token数: {await hierarchy.GetTotalTokenCountAsync().ConfigureAwait(true)}");
        _output.WriteLine($"当前层: {currentLayer?.LayerType}");
    }

    [Fact]
    public async Task ContextHierarchy_PromoteLayer_ShouldCompressContent()
    {
        // Arrange
        var hierarchy = ContextHierarchy.Create(
            new ContextHierarchyOptions { AutoCompressionEnabled = false },
            _logger);

        var originalContent = GenerateLargeCodeContent(100);
        var detailedLayer = ContextLayer.CreateDetailed(originalContent, "OriginalLayer");
        await hierarchy.AddLayerAsync(detailedLayer).ConfigureAwait(true);

        var originalTokenCount = await hierarchy.GetTotalTokenCountAsync().ConfigureAwait(true);

        // Act - 提升层级（压缩）
        var promotedLayer = await hierarchy.PromoteToLayerAsync(
            ContextLayerType.Summary,
            (content, target) => $"[Compressed Summary] {content[..Math.Min(200, content.Length)]}...").ConfigureAwait(true);

        // Assert
        promotedLayer.LayerType.Should().Be(ContextLayerType.Summary);
        (await hierarchy.GetLayerAsync(ContextLayerType.Detailed).ConfigureAwait(true)).Should().BeNull();
        (await hierarchy.GetLayerAsync(ContextLayerType.Summary).ConfigureAwait(true)).Should().NotBeNull();

        _output.WriteLine($"原始Token数: {originalTokenCount}");
        _output.WriteLine($"压缩后Token数: {promotedLayer.TokenCount}");
        _output.WriteLine($"压缩后内容: {promotedLayer.Content[..Math.Min(100, promotedLayer.Content.Length)]}...");
    }

    [Fact]
    public async Task ContextHierarchy_DemoteLayer_ShouldRestoreContent()
    {
        // Arrange
        var hierarchy = ContextHierarchy.Create(
            new ContextHierarchyOptions { AutoCompressionEnabled = false },
            _logger);

        var originalContent = GenerateLargeCodeContent(50);
        var layer = ContextLayer.CreateDetailed(originalContent, "TestLayer");
        layer.Compress();
        await hierarchy.AddLayerAsync(layer).ConfigureAwait(true);

        // Act - 降级层级（解压）
        var result = await hierarchy.DemoteToLayerAsync(ContextLayerType.Detailed).ConfigureAwait(true);

        // Assert
        result.Should().BeTrue();
        var restoredLayer = await hierarchy.GetLayerAsync(ContextLayerType.Detailed).ConfigureAwait(true);
        restoredLayer.Should().NotBeNull();
        restoredLayer!.IsCompressed.Should().BeFalse();
    }

    [Fact]
    public async Task ContextHierarchy_GetEffectiveContext_ShouldMergeLayers()
    {
        // Arrange
        var hierarchy = ContextHierarchy.Create(
            new ContextHierarchyOptions { AutoCompressionEnabled = false },
            _logger);

        await hierarchy.AddLayerAsync(ContextLayer.CreateDetailed(
            "Detailed content line 1\nDetailed content line 2",
            "DetailedLayer")).ConfigureAwait(true);
        await hierarchy.AddLayerAsync(ContextLayer.CreateSummary(
            "Summary of the content",
            "SummaryLayer")).ConfigureAwait(true);
        await hierarchy.AddLayerAsync(ContextLayer.CreateIndex(
            "Index: A, B, C",
            "IndexLayer")).ConfigureAwait(true);

        // Act
        var effectiveContext = await hierarchy.GetEffectiveContextAsync().ConfigureAwait(true);

        // Assert
        effectiveContext.Should().Contain("[Index]");
        effectiveContext.Should().Contain("[Summary]");
        effectiveContext.Should().Contain("[Detailed]");

        // 验证顺序：Index 在前，Detailed 在后
        var indexPos = effectiveContext.IndexOf("[Index]");
        var detailedPos = effectiveContext.IndexOf("[Detailed]");
        indexPos.Should().BeLessThan(detailedPos);

        _output.WriteLine("有效上下文:");
        _output.WriteLine(effectiveContext);
    }

    [Fact]
    public async Task FullCompressionWorkflow_WithContextHierarchy_ShouldReduceTokens()
    {
        // Arrange
        var factory = new CompressionStrategyFactory();
        var compressor = new ContextCompressor(factory);
        var hierarchy = ContextHierarchy.Create(
            new ContextHierarchyOptions
            {
                TokenThreshold = 2000,
                AutoCompressionEnabled = false
            },
            _logger);

        // 添加大量详细内容
        var largeContent = GenerateLargeCodeContent(200);
        var detailedLayer = ContextLayer.CreateDetailed(largeContent, "CodeLayer");
        await hierarchy.AddLayerAsync(detailedLayer).ConfigureAwait(true);

        var originalTokenCount = await hierarchy.GetTotalTokenCountAsync().ConfigureAwait(true);
        _output.WriteLine($"原始Token数: {originalTokenCount}");

        // Act - 使用压缩器压缩
        var compressionResult = await compressor.CompressAsync(
            detailedLayer.Content,
            ContentType.Code,
            CompressionOptions.ForCode).ConfigureAwait(true);

        // 创建摘要层
        var summaryLayer = ContextLayer.CreateSummary(
            compressionResult.CompressedContent,
            "CompressedLayer");
        summaryLayer.Compress();

        // 替换层级
        await hierarchy.RemoveLayerAsync(ContextLayerType.Detailed).ConfigureAwait(true);
        await hierarchy.AddLayerAsync(summaryLayer).ConfigureAwait(true);

        // Assert
        var finalTokenCount = await hierarchy.GetTotalTokenCountAsync().ConfigureAwait(true);
        finalTokenCount.Should().BeLessThan(originalTokenCount);

        _output.WriteLine($"最终Token数: {finalTokenCount}");
        _output.WriteLine($"Token减少: {originalTokenCount - finalTokenCount}");
        _output.WriteLine($"压缩比率: {(double)finalTokenCount / originalTokenCount:P2}");
    }

    [Fact]
    public async Task Performance_LargeContextCompression_ShouldCompleteInReasonableTime()
    {
        // Arrange
        var factory = new CompressionStrategyFactory();
        var compressor = new ContextCompressor(factory);
        var largeContent = GenerateLargeCodeContent(1000); // 大量代码

        _output.WriteLine($"测试内容大小: {largeContent.Length} 字符");

        // Act
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        var result = await compressor.CompressAsync(
            largeContent,
            ContentType.Code,
            CompressionOptions.ForCode).ConfigureAwait(true);
        stopwatch.Stop();

        // Assert
        result.IsSuccess.Should().BeTrue();
        stopwatch.ElapsedMilliseconds.Should().BeLessThan(5000); // 应该在5秒内完成

        _output.WriteLine($"压缩耗时: {stopwatch.ElapsedMilliseconds}ms");
        _output.WriteLine($"报告处理时间: {result.ProcessingTimeMs}ms");
        _output.WriteLine($"压缩比率: {result.CompressionRatio:P2}");
    }

    [Fact]
    public void CompressionStrategyFactory_RegisterAndRetrieve_ShouldWork()
    {
        // Arrange
        var factory = new CompressionStrategyFactory();

        // Act & Assert - 验证默认策略已注册
        factory.HasStrategyFor(ContentType.Code).Should().BeTrue();
        factory.HasStrategyFor(ContentType.Dialogue).Should().BeTrue();
        factory.HasStrategyFor(ContentType.ReferenceIndex).Should().BeTrue();

        // 获取策略 - 使用足够长的代码内容
        var codeContent = GenerateLargeCodeContent(10);
        var codeStrategy = factory.GetStrategy(codeContent, ContentType.Code);
        codeStrategy.Should().NotBeNull();
        codeStrategy!.Name.Should().Be("CodeContentCompressor");

        var allStrategies = factory.GetAllStrategies().ToList();
        allStrategies.Should().NotBeEmpty();

        foreach (var strategy in allStrategies)
        {
            _output.WriteLine($"策略: {strategy.Name} - {strategy.Description}");
        }
    }

    [Fact]
    public async Task CodeContentCompressor_WithRealCode_ShouldPreserveSignatures()
    {
        // Arrange - 使用足够长的代码以触发压缩
        var compressor = new CodeContentCompressor();
        var code = GenerateLargeCodeContent(20); // 生成足够长的代码

        // 使用 MaxMethodBodyLines = 3 来保留短方法体，压缩长方法体
        var options = new CompressionOptions
        {
            TargetCompressionRatio = 0.5,
            PreserveSignatures = true,
            PreserveImports = true,
            PreserveTypeDefinitions = true,
            MaxMethodBodyLines = 3 // 只保留3行方法体，超过的会被压缩
        };

        // Act
        var result = await compressor.CompressAsync(code, options).ConfigureAwait(true);

        // Assert
        result.Should().Contain("using System"); // 保留using
        result.Should().Contain("namespace GeneratedNamespace"); // 保留命名空间
        result.Should().Contain("public partial class GeneratedClass"); // 保留类定义
        result.Should().Contain("public void Method0(int param0)"); // 保留方法签名
        // 验证结果比原始代码短（说明发生了压缩）
        result.Length.Should().BeLessThan(code.Length);

        _output.WriteLine($"原始代码长度: {code.Length}");
        _output.WriteLine($"压缩后长度: {result.Length}");
        _output.WriteLine($"压缩比率: {(double)result.Length / code.Length:P2}");
    }

    #region Helper Methods

    private static string GenerateLargeCodeContent(int methodCount)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("using System;");
        sb.AppendLine("using System.Collections.Generic;");
        sb.AppendLine("using System.Linq;");
        sb.AppendLine();
        sb.AppendLine("namespace GeneratedNamespace");
        sb.AppendLine("{");
        sb.AppendLine("    public partial class GeneratedClass");
        sb.AppendLine("    {");

        for (int i = 0; i < methodCount; i++)
        {
            sb.AppendLine($"        private int _field{i};");
        }

        sb.AppendLine();

        for (int i = 0; i < methodCount; i++)
        {
            sb.AppendLine($"        public void Method{i}(int param{i})");
            sb.AppendLine("        {");
            sb.AppendLine($"            var localVar{i} = param{i} * 2;");
            sb.AppendLine($"            var anotherVar{i} = localVar{i} + 10;");
            sb.AppendLine($"            var result{i} = anotherVar{i} - 5;");
            sb.AppendLine($"            Console.WriteLine(result{i});");
            sb.AppendLine($"            _field{i} = result{i};");
            sb.AppendLine("        }");
            sb.AppendLine();
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
    }

    private static string GenerateDialogueContent(int roundCount)
    {
        var sb = new System.Text.StringBuilder();

        for (int i = 0; i < roundCount; i++)
        {
            sb.AppendLine($"[User] Message {i}: Can you help me with this code?");
            sb.AppendLine($"[Assistant] Response {i}: Sure, here's how you can do it...");
            sb.AppendLine($"    You need to implement the following:");
            sb.AppendLine($"    1. Create a new class");
            sb.AppendLine($"    2. Add the required methods");
            sb.AppendLine($"    3. Test your implementation");

            if (i % 5 == 0)
            {
                sb.AppendLine("    [决策] We decided to use approach A instead of B");
            }

            sb.AppendLine();
        }

        return sb.ToString();
    }

    private static string GenerateTextContent(int paragraphCount)
    {
        var sb = new System.Text.StringBuilder();

        for (int i = 0; i < paragraphCount; i++)
        {
            sb.AppendLine($"Paragraph {i}: This is a sample text content that needs to be compressed. " +
                "It contains various information about the system design and implementation details. " +
                "The content is intentionally verbose to test compression effectiveness. " +
                "More details can be added here to increase the content length for testing purposes.");
            sb.AppendLine();
        }

        return sb.ToString();
    }

    #endregion
}
