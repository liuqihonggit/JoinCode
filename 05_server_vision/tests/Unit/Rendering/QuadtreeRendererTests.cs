namespace JoinCode.Vision.Tests;

/// <summary>
/// 四叉树渲染器单元测试 — SkiaSharp 虚线叠加/裁剪缩放
/// </summary>
public sealed class QuadtreeRendererTests
{
    private readonly QuadtreeRenderer _renderer = new(new QuadtreeEncoder());

    [Fact]
    public async Task RenderAsync_AllCellsHidden_ReturnsValidSameSizeImage()
    {
        var base64 = CreateTestImageBase64(100, 100, Color.Red);
        var grid = new QuadtreeEncoder().BuildGrid(100, 100, 1);

        var result = await _renderer.RenderAsync(base64, grid);

        result.MediaType.Should().Be("image/png");
        var bytes = Convert.FromBase64String(result.RenderedBase64);
        using var img = Image.Load(bytes);
        img.Width.Should().Be(100);
        img.Height.Should().Be(100);
    }

    [Fact]
    public async Task RenderAsync_PaintedCell_ReturnsOverlayImage()
    {
        var base64 = CreateTestImageBase64(100, 100, Color.White);
        var encoder = new QuadtreeEncoder();
        var grid = encoder.BuildGrid(100, 100, 1);
        var painted = encoder.PaintCells(grid, new Dictionary<string, double> { ["L0.2"] = 0.8 });

        var result = await _renderer.RenderAsync(base64, painted);

        var bytes = Convert.FromBase64String(result.RenderedBase64);
        using var img = Image.Load(bytes);
        img.Width.Should().Be(100);
        img.Height.Should().Be(100);
    }

    [Fact]
    public async Task RenderAsync_MultiplePaintedCells_Succeeds()
    {
        var base64 = CreateTestImageBase64(100, 100, Color.White);
        var encoder = new QuadtreeEncoder();
        var grid = encoder.BuildGrid(100, 100, 2);
        var painted = encoder.PaintCells(grid, new Dictionary<string, double> { ["L0.2.1"] = 0.5, ["L0.1.0"] = 1.0 });

        var result = await _renderer.RenderAsync(base64, painted);

        result.RenderedBase64.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ZoomAsync_ReturnsCroppedSubimageAndNewGrid()
    {
        var base64 = CreateTestImageBase64(100, 100, Color.Red);

        var result = await _renderer.ZoomAsync(base64, "L0.2", 100, 100, 1, 1);

        result.SourceCellCode.Should().Be("L0.2");
        result.Grid.ImageWidth.Should().Be(50);
        result.Grid.ImageHeight.Should().Be(50);
        result.Grid.Depth.Should().Be(1);
        result.Grid.Cells.Should().HaveCount(4);

        var bytes = Convert.FromBase64String(result.SubImageBase64);
        using var img = Image.Load(bytes);
        img.Width.Should().Be(50);
        img.Height.Should().Be(50);
    }

    [Fact]
    public async Task ZoomAsync_Depth2_CropsQuarterRegion()
    {
        var base64 = CreateTestImageBase64(100, 100, Color.Blue);

        var result = await _renderer.ZoomAsync(base64, "L0.2", 100, 100, 2, 1);

        result.Grid.ImageWidth.Should().Be(25);
        result.Grid.ImageHeight.Should().Be(25);
        var bytes = Convert.FromBase64String(result.SubImageBase64);
        using var img = Image.Load(bytes);
        img.Width.Should().Be(25);
        img.Height.Should().Be(25);
    }

    [Fact]
    public async Task RenderAsync_InvalidBase64_Throws()
    {
        var grid = new QuadtreeEncoder().BuildGrid(100, 100, 1);
        var act = async () => await _renderer.RenderAsync("not-valid-base64!!!", grid);
        await act.Should().ThrowAsync<ArgumentException>().WithMessage("*VIS020*");
    }

    private static string CreateTestImageBase64(int width, int height, Color color)
    {
        using var img = new Image<Rgba32>(width, height, color);
        using var ms = new MemoryStream();
        img.Save(ms, PngFormat.Instance);
        return Convert.ToBase64String(ms.ToArray());
    }
}
