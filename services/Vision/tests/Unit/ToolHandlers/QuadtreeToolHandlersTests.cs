namespace Vision.Tests.ToolHandlers;

/// <summary>
/// QuadtreeToolHandlers 单元测试 — 验证 M1 的 6 个 MCP 工具
/// </summary>
public sealed class QuadtreeToolHandlersTests
{
    private readonly QuadtreeToolHandlers _handlers;
    private const int TestWidth = 8;
    private const int TestHeight = 8;

    public QuadtreeToolHandlersTests()
    {
        var annotator = new QuadtreeEncoder();
        var renderer = new QuadtreeRenderer(annotator);
        _handlers = new QuadtreeToolHandlers(annotator, renderer);
    }

    private static string CreateTestImageBase64(int width = TestWidth, int height = TestHeight)
    {
        using var image = new Image<Rgb24>(width, height, new Rgb24(100, 150, 200));
        using var ms = new MemoryStream();
        image.Save(ms, PngFormat.Instance);
        return Convert.ToBase64String(ms.ToArray());
    }

    [Fact]
    public async Task QuadtreeBuild_ShouldReturnGridWithAllCells()
    {
        var base64 = CreateTestImageBase64();
        var result = await _handlers.QuadtreeBuildAsync(base64, depth: 1);

        result.IsError.Should().BeFalse();
        result.Content.Should().NotBeEmpty();
        var text = result.Content[0].Text!;
        text.Should().Contain("8x8");
        text.Should().Contain("层数: 1");
        text.Should().Contain("4 格");
        text.Should().Contain("L0.0");
        text.Should().Contain("L0.1");
        text.Should().Contain("L0.2");
        text.Should().Contain("L0.3");
    }

    [Fact]
    public async Task QuadtreeBuild_Depth2_ShouldReturn16Cells()
    {
        var base64 = CreateTestImageBase64();
        var result = await _handlers.QuadtreeBuildAsync(base64, depth: 2);

        result.IsError.Should().BeFalse();
        var text = result.Content[0].Text!;
        text.Should().Contain("16 格");
        text.Should().Contain("L0.0.0");
        text.Should().Contain("L0.3.3");
    }

    [Fact]
    public async Task QuadtreeBuild_EmptyBase64_ShouldReturnError()
    {
        var result = await _handlers.QuadtreeBuildAsync("", depth: 1);
        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("[VIS100]");
    }

    [Fact]
    public async Task QuadtreeBuild_NegativeDepth_ShouldReturnError()
    {
        var base64 = CreateTestImageBase64();
        var result = await _handlers.QuadtreeBuildAsync(base64, depth: -1);
        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("[VIS101]");
    }

    [Fact]
    public async Task QuadtreeZoom_ShouldReturnSubImageAndNewGrid()
    {
        var base64 = CreateTestImageBase64();
        var result = await _handlers.QuadtreeZoomAsync(base64, "L0.0", sourceDepth: 1, targetDepth: 1);

        result.IsError.Should().BeFalse();
        result.Content.Should().HaveCount(2);
        result.Content[0].Type.Should().Be(ToolContentType.Text);
        result.Content[1].Type.Should().Be(ToolContentType.Image);
        result.Content[0].Text.Should().Contain("聚焦格子 L0.0");
        result.Content[1].Data.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task QuadtreeZoom_EmptyCellCode_ShouldReturnError()
    {
        var base64 = CreateTestImageBase64();
        var result = await _handlers.QuadtreeZoomAsync(base64, "", sourceDepth: 1);
        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("[VIS111]");
    }

    [Fact]
    public async Task QuadtreePaint_ShouldUpdateAlphaValues()
    {
        var paintsJson = """{"L0.0":0.5,"L0.1":0.8}""";
        var result = await _handlers.QuadtreePaintAsync(TestWidth, TestHeight, depth: 1, paintsJson);

        result.IsError.Should().BeFalse();
        var text = result.Content[0].Text!;
        text.Should().Contain("alpha=0.50");
        text.Should().Contain("alpha=0.80");
        text.Should().Contain("alpha=-");
    }

    [Fact]
    public async Task QuadtreePaint_EmptyPaintsJson_ShouldReturnError()
    {
        var result = await _handlers.QuadtreePaintAsync(TestWidth, TestHeight, depth: 1, "");
        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("[VIS121]");
    }

    [Fact]
    public async Task QuadtreePaint_InvalidDimensions_ShouldReturnError()
    {
        var result = await _handlers.QuadtreePaintAsync(0, TestHeight, depth: 1, """{"L0.0":0.5}""");
        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("[VIS120]");
    }

    [Fact]
    public async Task QuadtreeRender_WithPaints_ShouldReturnRenderedImage()
    {
        var base64 = CreateTestImageBase64();
        var paintsJson = """{"L0.0":0.5}""";
        var result = await _handlers.QuadtreeRenderAsync(base64, TestWidth, TestHeight, depth: 1, paintsJson);

        result.IsError.Should().BeFalse();
        result.Content.Should().HaveCount(2);
        result.Content[1].Type.Should().Be(ToolContentType.Image);
        result.Content[1].Data.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task QuadtreeRender_NoPaints_ShouldShowAllGrid()
    {
        var base64 = CreateTestImageBase64();
        var result = await _handlers.QuadtreeRenderAsync(base64, TestWidth, TestHeight, depth: 1);

        result.IsError.Should().BeFalse();
        result.Content.Should().HaveCount(2);
        result.Content[1].Type.Should().Be(ToolContentType.Image);
        result.Content[1].Data.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task QuadtreeRender_EmptyBase64_ShouldReturnError()
    {
        var result = await _handlers.QuadtreeRenderAsync("", TestWidth, TestHeight, depth: 1);
        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("[VIS130]");
    }

    [Theory]
    [InlineData("N", "L0.0", "L0.2")]
    [InlineData("S", "L0.2", "L0.0")]
    [InlineData("W", "L0.1", "L0.0")]
    [InlineData("E", "L0.0", "L0.1")]
    public async Task QuadtreeNeighbor_ShouldReturnNeighborCode(string direction, string fromCell, string expectedNeighbor)
    {
        var result = await _handlers.QuadtreeNeighborAsync(fromCell, direction, TestWidth, TestHeight, depth: 1);

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain(expectedNeighbor);
    }

    [Fact]
    public async Task QuadtreeNeighbor_OutOfBounds_ShouldReturnNullMessage()
    {
        var result = await _handlers.QuadtreeNeighborAsync("L0.0", "W", TestWidth, TestHeight, depth: 1);

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("越界");
    }

    [Fact]
    public async Task QuadtreeNeighbor_InvalidDirection_ShouldReturnError()
    {
        var result = await _handlers.QuadtreeNeighborAsync("L0.0", "XX", TestWidth, TestHeight, depth: 1);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("[VIS141]");
    }

    [Fact]
    public async Task ScreenIndicate_ShouldReturnHighlightedImage()
    {
        var base64 = CreateTestImageBase64();
        var result = await _handlers.ScreenIndicateAsync(base64, "L0.0", TestWidth, TestHeight, depth: 1);

        result.IsError.Should().BeFalse();
        result.Content.Should().HaveCount(2);
        result.Content[0].Text.Should().Contain("高亮区域: L0.0");
        result.Content[1].Type.Should().Be(ToolContentType.Image);
        result.Content[1].Data.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ScreenIndicate_EmptyBase64_ShouldReturnError()
    {
        var result = await _handlers.ScreenIndicateAsync("", "L0.0", TestWidth, TestHeight, depth: 1);
        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("[VIS150]");
    }
}
