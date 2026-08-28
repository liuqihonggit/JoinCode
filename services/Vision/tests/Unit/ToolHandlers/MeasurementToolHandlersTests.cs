namespace Vision.Tests.ToolHandlers;

/// <summary>
/// MeasurementToolHandlers 单元测试 — 验证 M4 的 3 个 MCP 工具
/// </summary>
public sealed class MeasurementToolHandlersTests
{
    private static string CreateTestImageBase64(int width = 8, int height = 8)
    {
        using var image = new Image<Rgb24>(width, height, new Rgb24(100, 150, 200));
        using var ms = new MemoryStream();
        image.Save(ms, PngFormat.Instance);
        return Convert.ToBase64String(ms.ToArray());
    }

    [Fact]
    public async Task MeasureLength_HorizontalLine_ShouldCalculateCorrectly()
    {
        var handlers = new MeasurementToolHandlers();
        var result = await handlers.MeasureLengthAsync(0, 0, 10, 0);

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("10.00 px");
        result.Content[0].Text.Should().Contain("0.0°");
    }

    [Fact]
    public async Task MeasureLength_VerticalLine_ShouldCalculateCorrectly()
    {
        var handlers = new MeasurementToolHandlers();
        var result = await handlers.MeasureLengthAsync(0, 0, 0, 10);

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("10.00 px");
        result.Content[0].Text.Should().Contain("90.0°");
    }

    [Fact]
    public async Task MeasureLength_Diagonal_ShouldCalculateCorrectly()
    {
        var handlers = new MeasurementToolHandlers();
        var result = await handlers.MeasureLengthAsync(0, 0, 3, 4);

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("5.00 px");
    }

    [Fact]
    public async Task MeasureDepth_ValidRegion_ShouldReturnAnalysis()
    {
        var base64 = CreateTestImageBase64();
        var handlers = new MeasurementToolHandlers();
        var result = await handlers.MeasureDepthAsync(base64, 0, 0, 4, 4);

        result.IsError.Should().BeFalse();
        var text = result.Content[0].Text!;
        text.Should().Contain("颜色进深分析");
        text.Should().Contain("平均颜色");
        text.Should().Contain("深度估计");
    }

    [Fact]
    public async Task MeasureDepth_EmptyBase64_ShouldReturnError()
    {
        var handlers = new MeasurementToolHandlers();
        var result = await handlers.MeasureDepthAsync("", 0, 0, 4, 4);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("[VIS400]");
    }

    [Fact]
    public async Task MeasureDepth_RegionOutOfRange_ShouldReturnError()
    {
        var base64 = CreateTestImageBase64(8, 8);
        var handlers = new MeasurementToolHandlers();
        var result = await handlers.MeasureDepthAsync(base64, 0, 0, 100, 100);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("[VIS402]");
    }

    [Fact]
    public async Task MeasureRatio_Square_ShouldReturnRatio1()
    {
        var handlers = new MeasurementToolHandlers();
        var result = await handlers.MeasureRatioAsync(100, 100);

        result.IsError.Should().BeFalse();
        var text = result.Content[0].Text!;
        text.Should().Contain("1.0000");
        text.Should().Contain("1:1");
    }

    [Fact]
    public async Task MeasureRatio_Widescreen16to9_ShouldIdentifyCommonRatio()
    {
        var handlers = new MeasurementToolHandlers();
        var result = await handlers.MeasureRatioAsync(1920, 1080);

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("16:9");
    }

    [Fact]
    public async Task MeasureRatio_Traditional4to3_ShouldIdentifyCommonRatio()
    {
        var handlers = new MeasurementToolHandlers();
        var result = await handlers.MeasureRatioAsync(800, 600);

        result.IsError.Should().BeFalse();
        result.Content[0].Text.Should().Contain("4:3");
    }

    [Fact]
    public async Task MeasureRatio_InvalidDimensions_ShouldReturnError()
    {
        var handlers = new MeasurementToolHandlers();
        var result = await handlers.MeasureRatioAsync(0, 100);

        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("[VIS410]");
    }
}
