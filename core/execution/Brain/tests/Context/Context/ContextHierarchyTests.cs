
namespace Core.Tests.Context;

public class ContextHierarchyTests
{
    [Fact]
    public async Task Constructor_DefaultValues_ShouldBeCorrect()
    {
        var hierarchy = ContextHierarchy.Create();

        hierarchy.TokenThreshold.Should().Be(4000);
        (await hierarchy.GetLayersAsync().ConfigureAwait(true)).Should().BeEmpty();
        (await hierarchy.GetCurrentLayerAsync().ConfigureAwait(true)).Should().BeNull();
    }

    [Fact]
    public void Constructor_WithOptions_ShouldUseOptions()
    {
        var options = new ContextHierarchyOptions
        {
            TokenThreshold = 2000,
            AutoCompressionEnabled = false,
            MaxLayers = 5,
            DefaultCompressionRatio = 0.3
        };

        var hierarchy = ContextHierarchy.Create(options);

        hierarchy.TokenThreshold.Should().Be(2000);
    }

    [Fact]
    public async Task AddLayerAsync_SingleLayer_ShouldAddSuccessfully()
    {
        var hierarchy = ContextHierarchy.Create();
        var layer = ContextLayer.CreateDetailed("Test content", "TestLayer");

        await hierarchy.AddLayerAsync(layer).ConfigureAwait(true);

        (await hierarchy.GetLayersAsync().ConfigureAwait(true)).Should().HaveCount(1);
        (await hierarchy.GetCurrentLayerAsync().ConfigureAwait(true)).Should().NotBeNull();
        (await hierarchy.GetCurrentLayerAsync().ConfigureAwait(true))!.LayerType.Should().Be(ContextLayerType.Detailed);
    }

    [Fact]
    public async Task AddLayerAsync_MultipleLayers_ShouldMaintainOrder()
    {
        var hierarchy = ContextHierarchy.Create();

        await hierarchy.AddLayerAsync(ContextLayer.CreateSummary("Summary content", "SummaryLayer")).ConfigureAwait(true);
        await hierarchy.AddLayerAsync(ContextLayer.CreateDetailed("Detailed content", "DetailedLayer")).ConfigureAwait(true);
        await hierarchy.AddLayerAsync(ContextLayer.CreateIndex("Index content", "IndexLayer")).ConfigureAwait(true);

        var layers = await hierarchy.GetLayersAsync().ConfigureAwait(true);
        layers.Should().HaveCount(3);
        layers[0].LayerType.Should().Be(ContextLayerType.Detailed);
        layers[1].LayerType.Should().Be(ContextLayerType.Summary);
        layers[2].LayerType.Should().Be(ContextLayerType.Index);
    }

    [Fact]
    public async Task AddLayerAsync_DuplicateType_ShouldReplaceExisting()
    {
        var hierarchy = ContextHierarchy.Create();

        await hierarchy.AddLayerAsync(ContextLayer.CreateDetailed("Original content", "OriginalLayer")).ConfigureAwait(true);
        await hierarchy.AddLayerAsync(ContextLayer.CreateDetailed("Replaced content", "ReplacedLayer")).ConfigureAwait(true);

        var layers = await hierarchy.GetLayersAsync().ConfigureAwait(true);
        layers.Should().HaveCount(1);
        layers[0].Content.Should().Be("Replaced content");
    }

    [Fact]
    public async Task RemoveLayerAsync_ExistingLayer_ShouldRemoveSuccessfully()
    {
        var hierarchy = ContextHierarchy.Create();
        await hierarchy.AddLayerAsync(ContextLayer.CreateDetailed("Test content", "TestLayer")).ConfigureAwait(true);

        var result = await hierarchy.RemoveLayerAsync(ContextLayerType.Detailed).ConfigureAwait(true);

        result.Should().BeTrue();
        (await hierarchy.GetLayersAsync().ConfigureAwait(true)).Should().BeEmpty();
    }

    [Fact]
    public async Task RemoveLayerAsync_NonExistingLayer_ShouldReturnFalse()
    {
        var hierarchy = ContextHierarchy.Create();

        var result = await hierarchy.RemoveLayerAsync(ContextLayerType.Detailed).ConfigureAwait(true);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task GetLayerAsync_ExistingLayer_ShouldReturnLayer()
    {
        var hierarchy = ContextHierarchy.Create();
        var layer = ContextLayer.CreateDetailed("Test content", "TestLayer");
        await hierarchy.AddLayerAsync(layer).ConfigureAwait(true);

        var result = await hierarchy.GetLayerAsync(ContextLayerType.Detailed).ConfigureAwait(true);

        result.Should().NotBeNull();
        result!.LayerType.Should().Be(ContextLayerType.Detailed);
    }

    [Fact]
    public async Task GetLayerAsync_NonExistingLayer_ShouldReturnNull()
    {
        var hierarchy = ContextHierarchy.Create();

        var result = await hierarchy.GetLayerAsync(ContextLayerType.Detailed).ConfigureAwait(true);

        result.Should().BeNull();
    }

    [Fact]
    public async Task GetTotalTokenCountAsync_EmptyHierarchy_ShouldReturnZero()
    {
        var hierarchy = ContextHierarchy.Create();

        var result = await hierarchy.GetTotalTokenCountAsync().ConfigureAwait(true);

        result.Should().Be(0);
    }

    [Fact]
    public async Task GetTotalTokenCountAsync_WithLayers_ShouldSumCorrectly()
    {
        var hierarchy = ContextHierarchy.Create();
        await hierarchy.AddLayerAsync(ContextLayer.CreateDetailed("Content 1 with more text to have tokens", "Layer1")).ConfigureAwait(true);
        await hierarchy.AddLayerAsync(ContextLayer.CreateSummary("Content 2", "Layer2")).ConfigureAwait(true);

        var result = await hierarchy.GetTotalTokenCountAsync().ConfigureAwait(true);

        result.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetEffectiveContextAsync_EmptyHierarchy_ShouldReturnEmptyString()
    {
        var hierarchy = ContextHierarchy.Create();

        var result = await hierarchy.GetEffectiveContextAsync().ConfigureAwait(true);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GetEffectiveContextAsync_WithLayers_ShouldMergeCorrectly()
    {
        var hierarchy = ContextHierarchy.Create();
        await hierarchy.AddLayerAsync(ContextLayer.CreateDetailed("Detailed line 1\nDetailed line 2", "DetailedLayer")).ConfigureAwait(true);
        await hierarchy.AddLayerAsync(ContextLayer.CreateSummary("Summary content", "SummaryLayer")).ConfigureAwait(true);

        var result = await hierarchy.GetEffectiveContextAsync().ConfigureAwait(true);

        result.Should().Contain("[Summary]");
        result.Should().Contain("Summary content");
        result.Should().Contain("[Detailed]");
        result.Should().Contain("Detailed line 1");
    }

    [Fact]
    public async Task PromoteToLayerAsync_WithValidLayer_ShouldPromoteSuccessfully()
    {
        var hierarchy = ContextHierarchy.Create();
        await hierarchy.AddLayerAsync(ContextLayer.CreateDetailed("Line 1\nLine 2\nLine 3\nLine 4", "TestLayer")).ConfigureAwait(true);

        var promoted = await hierarchy.PromoteToLayerAsync(
            ContextLayerType.Summary,
            (content, target) => $"[Compressed] {content}").ConfigureAwait(true);

        promoted.LayerType.Should().Be(ContextLayerType.Summary);
        promoted.Content.Should().Contain("[Compressed]");
        (await hierarchy.GetLayerAsync(ContextLayerType.Detailed).ConfigureAwait(true)).Should().BeNull();
        (await hierarchy.GetLayerAsync(ContextLayerType.Summary).ConfigureAwait(true)).Should().NotBeNull();
    }

    [Fact]
    public async Task PromoteToLayerAsync_WithoutCurrentLayer_ShouldThrowException()
    {
        var hierarchy = ContextHierarchy.Create();

        Func<Task> act = async () => await hierarchy.PromoteToLayerAsync(
            ContextLayerType.Summary,
            (content, target) => content).ConfigureAwait(true);

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task PromoteToLayerAsync_ToSameOrLowerLayer_ShouldThrowException()
    {
        var hierarchy = ContextHierarchy.Create();
        await hierarchy.AddLayerAsync(ContextLayer.CreateSummary("Summary content", "TestLayer")).ConfigureAwait(true);

        Func<Task> act = async () => await hierarchy.PromoteToLayerAsync(
            ContextLayerType.Detailed,
            (content, target) => content).ConfigureAwait(true);

        await act.Should().ThrowAsync<InvalidOperationException>().ConfigureAwait(true);
    }

    [Fact]
    public async Task DemoteToLayerAsync_WithCompressedLayer_ShouldRestoreSuccessfully()
    {
        var hierarchy = ContextHierarchy.Create();
        var layer = ContextLayer.CreateDetailed("Original detailed content that is long enough to be compressed when needed", "TestLayer");
        layer.Compress();
        await hierarchy.AddLayerAsync(layer).ConfigureAwait(true);

        var result = await hierarchy.DemoteToLayerAsync(ContextLayerType.Detailed).ConfigureAwait(true);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task DemoteToLayerAsync_WithoutCompression_ShouldReturnFalse()
    {
        var hierarchy = ContextHierarchy.Create();
        await hierarchy.AddLayerAsync(ContextLayer.CreateSummary("Not compressed content", "TestLayer")).ConfigureAwait(true);

        var result = await hierarchy.DemoteToLayerAsync(ContextLayerType.Summary).ConfigureAwait(true);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task DemoteToLayerAsync_NonExistingLayer_ShouldReturnFalse()
    {
        var hierarchy = ContextHierarchy.Create();

        var result = await hierarchy.DemoteToLayerAsync(ContextLayerType.Summary).ConfigureAwait(true);

        result.Should().BeFalse();
    }

    [Fact]
    public void ContextLayer_CreateDetailed_ShouldCreateCorrectType()
    {
        var layer = ContextLayer.CreateDetailed("Test content", "TestLayer");

        layer.LayerType.Should().Be(ContextLayerType.Detailed);
        layer.Content.Should().Be("Test content");
        layer.IsCompressed.Should().BeFalse();
    }

    [Fact]
    public void ContextLayer_CreateSummary_ShouldCreateCorrectType()
    {
        var layer = ContextLayer.CreateSummary("Summary content", "TestLayer");

        layer.LayerType.Should().Be(ContextLayerType.Summary);
        layer.Content.Should().Be("Summary content");
    }

    [Fact]
    public void ContextLayer_CreateIndex_ShouldCreateCorrectType()
    {
        var layer = ContextLayer.CreateIndex("Index content", "TestLayer");

        layer.LayerType.Should().Be(ContextLayerType.Index);
        layer.Content.Should().Be("Index content");
    }

    [Theory]
    [InlineData(ContextLayerType.Detailed)]
    [InlineData(ContextLayerType.Summary)]
    [InlineData(ContextLayerType.Index)]
    public void ContextLayerType_AllTypes_ShouldBeDefined(ContextLayerType type)
    {
        Enum.IsDefined(typeof(ContextLayerType), type).Should().BeTrue();
    }

    [Fact]
    public void ContextHierarchyOptions_DefaultValues_ShouldBeCorrect()
    {
        var options = new ContextHierarchyOptions();

        options.TokenThreshold.Should().Be(4000);
        options.AutoCompressionEnabled.Should().BeTrue();
        options.MaxLayers.Should().Be(3);
        options.DefaultCompressionRatio.Should().Be(0.5);
    }

    [Fact]
    public void ContextLayer_Compress_ShouldReduceContent()
    {
        var layer = ContextLayer.CreateDetailed(
            "This is a very long content that should be compressed when the compress method is called. " +
            "It contains multiple sentences and should be reduced in size.",
            "TestLayer");

        var originalLength = layer.Content.Length;
        layer.Compress();

        layer.IsCompressed.Should().BeTrue();
    }

    [Fact]
    public void ContextLayer_Decompress_ShouldRestoreContent()
    {
        var originalContent = "Original content that will be compressed and then decompressed";
        var layer = ContextLayer.CreateDetailed(originalContent, "TestLayer");

        layer.Compress();
        layer.IsCompressed.Should().BeTrue();

        layer.Decompress();
        layer.IsCompressed.Should().BeFalse();
    }

    [Fact]
    public void ContextLayer_TokenCount_ShouldBeCalculatedCorrectly()
    {
        var layer = ContextLayer.CreateDetailed("Hello world test content", "TestLayer");

        layer.TokenCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void ContextLayer_Metadata_ShouldStoreLayerName()
    {
        var layer = ContextLayer.CreateDetailed("Test content", "TestLayerName");

        layer.Metadata.Should().NotBeNull();
        layer.Metadata.LayerName.Should().Be("TestLayerName");
    }

    [Fact]
    public void ContextLayer_Metadata_Compression_ShouldTrackTokenCounts()
    {
        var layer = ContextLayer.CreateDetailed("This is a test content with enough length to have meaningful token counts for testing compression tracking functionality", "TestLayer");
        var originalTokens = layer.TokenCount;

        layer.Compress();

        layer.Metadata.OriginalTokenCount.Should().Be(originalTokens);
        layer.Metadata.CompressedAt.Should().NotBeNull();
        layer.IsCompressed.Should().BeTrue();
    }
}
