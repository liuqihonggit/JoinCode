
namespace Core.Tests.Context;

public class ContextLayerTests
{
    [Fact]
    public void ContextLayer_CreateDetailed_ShouldSetCorrectType()
    {
        var layer = ContextLayer.CreateDetailed("Test content", "TestLayer");

        Assert.Equal(ContextLayerType.Detailed, layer.LayerType);
        Assert.Equal("Test content", layer.Content);
        Assert.Equal("TestLayer", layer.Metadata.LayerName);
    }

    [Fact]
    public void ContextLayer_CreateSummary_ShouldSetCorrectType()
    {
        var layer = ContextLayer.CreateSummary("Summary content", "SummaryLayer");

        Assert.Equal(ContextLayerType.Summary, layer.LayerType);
        Assert.Equal("Summary content", layer.Content);
    }

    [Fact]
    public void ContextLayer_CreateIndex_ShouldSetCorrectType()
    {
        var layer = ContextLayer.CreateIndex("Index content", "IndexLayer");

        Assert.Equal(ContextLayerType.Index, layer.LayerType);
        Assert.Equal("Index content", layer.Content);
    }

    [Fact]
    public void ContextLayer_DefaultLayerName_ShouldBeGenerated()
    {
        var layer = ContextLayer.CreateDetailed("Content");

        Assert.NotNull(layer.Metadata.LayerName);
        Assert.Contains("Detailed", layer.Metadata.LayerName);
    }

    [Fact]
    public void ContextLayer_TokenCount_ShouldEstimateCorrectly()
    {
        var content = new string('a', 100);
        var layer = ContextLayer.CreateDetailed(content);

        Assert.Equal(25, layer.TokenCount);
    }

    [Fact]
    public void ContextLayer_TokenCount_EmptyContent_ShouldBeZero()
    {
        var layer = ContextLayer.CreateDetailed("");

        Assert.Equal(0, layer.TokenCount);
    }

    [Fact]
    public void ContextLayer_IsCompressed_Initially_ShouldBeFalse()
    {
        var layer = ContextLayer.CreateDetailed("Content");

        Assert.False(layer.IsCompressed);
    }

    [Fact]
    public void ContextLayer_Compress_DetailedLayer_ShouldCompress()
    {
        var longContent = new string('x', 500);
        var layer = ContextLayer.CreateDetailed(longContent, "TestLayer");
        var originalTokenCount = layer.TokenCount;

        layer.Compress();

        Assert.True(layer.IsCompressed);
        Assert.True(layer.TokenCount < originalTokenCount);
        Assert.NotNull(layer.Metadata.CompressedAt);
        Assert.True(layer.Metadata.CompressionRatio < 1.0);
    }

    [Fact]
    public void ContextLayer_Compress_ShortContent_ShouldNotChange()
    {
        var shortContent = "Short";
        var layer = ContextLayer.CreateDetailed(shortContent);

        layer.Compress();

        Assert.Equal(shortContent, layer.Content);
    }

    [Fact]
    public void ContextLayer_Compress_IndexLayer_ShouldNotCompress()
    {
        var longContent = new string('x', 500);
        var layer = ContextLayer.CreateIndex(longContent);

        layer.Compress();

        Assert.False(layer.IsCompressed);
        Assert.Equal(longContent, layer.Content);
    }

    [Fact]
    public void ContextLayer_Decompress_ShouldRestoreOriginal()
    {
        var longContent = new string('x', 500);
        var layer = ContextLayer.CreateDetailed(longContent);

        layer.Compress();
        var compressedContent = layer.Content;

        layer.Decompress();

        Assert.False(layer.IsCompressed);
        Assert.Equal(longContent, layer.Content);
        Assert.NotEqual(compressedContent, layer.Content);
    }

    [Fact]
    public void ContextLayer_Decompress_NotCompressed_ShouldReturnSame()
    {
        var layer = ContextLayer.CreateDetailed("Content");

        var result = layer.Decompress();

        Assert.Same(layer, result);
    }

    [Fact]
    public void ContextLayer_GetSummary_DetailedLayer_ShouldReturnCorrectFormat()
    {
        var layer = ContextLayer.CreateDetailed("Content", "DetailedLayer");

        var summary = layer.GetSummary();

        Assert.Contains("[详细]", summary);
        Assert.Contains("DetailedLayer", summary);
        Assert.Contains("tokens", summary);
    }

    [Fact]
    public void ContextLayer_GetSummary_SummaryLayer_ShouldIncludeCompressionRatio()
    {
        var longContent = new string('x', 500);
        var layer = ContextLayer.CreateSummary(longContent, "SummaryLayer");
        layer.Compress();

        var summary = layer.GetSummary();

        Assert.Contains("[摘要]", summary);
        Assert.Contains("SummaryLayer", summary);
        Assert.Contains("压缩比", summary);
    }

    [Fact]
    public void ContextLayer_GetSummary_IndexLayer_ShouldReturnCorrectFormat()
    {
        var layer = ContextLayer.CreateIndex("Content", "IndexLayer");

        var summary = layer.GetSummary();

        Assert.Contains("[索引]", summary);
        Assert.Contains("IndexLayer", summary);
    }

    [Fact]
    public void ContextLayer_Metadata_CreatedAt_ShouldBeSet()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var layer = ContextLayer.CreateDetailed("Content");
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.True(layer.Metadata.CreatedAt >= before);
        Assert.True(layer.Metadata.CreatedAt <= after);
    }

    [Fact]
    public void ContextLayer_Metadata_OriginalTokenCount_ShouldBeSet()
    {
        var content = new string('a', 100);
        var layer = ContextLayer.CreateDetailed(content);

        Assert.Equal(25, layer.Metadata.OriginalTokenCount);
    }

    [Fact]
    public void ContextLayer_Metadata_CompressionRatio_Initially_ShouldBeOne()
    {
        var layer = ContextLayer.CreateDetailed("Content");

        Assert.Equal(1.0, layer.Metadata.CompressionRatio);
    }

    [Fact]
    public void ContextLayer_Metadata_WithCompression_ShouldCreateNewInstance()
    {
        var original = new LayerMetadata("TestLayer")
        {
            OriginalTokenCount = 100
        };

        var compressed = original.WithCompression(50);

        Assert.NotSame(original, compressed);
        Assert.Equal("TestLayer", compressed.LayerName);
        Assert.Equal(100, compressed.OriginalTokenCount);
        Assert.Equal(50, compressed.CompressedTokenCount);
        Assert.NotNull(compressed.CompressedAt);
        Assert.Equal(0.5, compressed.CompressionRatio);
    }

    [Fact]
    public void ContextLayer_ToJson_ShouldSerializeCorrectly()
    {
        var layer = ContextLayer.CreateDetailed("Test content", "TestLayer");

        var json = layer.ToJson();

        Assert.Contains("\"layerType\":", json);
        Assert.Contains("Detailed", json);
        Assert.Contains("\"content\":", json);
        Assert.Contains("Test content", json);
        Assert.Contains("\"metadata\":", json);
    }

    [Fact]
    public void ContextLayer_FromJson_ShouldDeserializeCorrectly()
    {
        var original = ContextLayer.CreateDetailed("Test content", "TestLayer");
        var json = original.ToJson();

        var deserialized = ContextLayer.FromJson(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.LayerType, deserialized.LayerType);
        Assert.Equal(original.Content, deserialized.Content);
        Assert.Equal(original.Metadata.LayerName, deserialized.Metadata.LayerName);
    }

    [Fact]
    public void ContextLayer_FromJson_InvalidJson_ShouldReturnNull()
    {
        var result = ContextLayer.FromJson("invalid json");

        Assert.Null(result);
    }

    [Fact]
    public void ContextLayer_Content_SetValue_ShouldUpdate()
    {
        var layer = ContextLayer.CreateDetailed("Initial");

        layer.Content = "Updated";

        Assert.Equal("Updated", layer.Content);
    }

    [Fact]
    public void ContextLayer_Compress_AlreadyCompressed_ShouldReturnSame()
    {
        var longContent = new string('x', 500);
        var layer = ContextLayer.CreateDetailed(longContent);

        layer.Compress();
        var firstCompressed = layer.Content;

        layer.Compress();
        var secondCompressed = layer.Content;

        Assert.Equal(firstCompressed, secondCompressed);
    }

    [Theory]
    [InlineData(ContextLayerType.Detailed)]
    [InlineData(ContextLayerType.Summary)]
    [InlineData(ContextLayerType.Index)]
    public void ContextLayer_AllTypes_ShouldBeSerializable(ContextLayerType layerType)
    {
        var layer = new ContextLayer(layerType, "Test content", "TestLayer");

        var json = layer.ToJson();
        var deserialized = ContextLayer.FromJson(json);

        Assert.NotNull(deserialized);
        Assert.Equal(layerType, deserialized.LayerType);
    }
}
