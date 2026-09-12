namespace JoinCode.Vision.Tests;

/// <summary>
/// 四叉树桌面叠加坐标转换器单元测试 — 格子坐标→屏幕坐标转换(ADR 0032 延伸应用)
/// </summary>
public sealed class QuadtreeDesktopOverlayMapperTests
{
    private readonly QuadtreeEncoder _encoder = new();
    private readonly QuadtreeDesktopOverlayMapper _mapper = new();

    [Fact]
    public void MapToScreen_AllHiddenCells_ReturnsEmpty()
    {
        var grid = _encoder.BuildGrid(100, 100, 1);
        var result = _mapper.MapToScreen(grid, 0, 0);
        result.Should().BeEmpty();
    }

    [Fact]
    public void MapToScreen_SingleVisibleCell_AtOrigin_ReturnsCellCoordinates()
    {
        var grid = _encoder.BuildGrid(100, 100, 1);
        var painted = _encoder.PaintCells(grid, new Dictionary<string, double> { ["L0.2"] = 1.0 });
        var result = _mapper.MapToScreen(painted, 0, 0);

        result.Should().HaveCount(1);
        result[0].CellCode.Should().Be("L0.2");
        result[0].ScreenX.Should().Be(0);
        result[0].ScreenY.Should().Be(0);
        result[0].Width.Should().Be(50);
        result[0].Height.Should().Be(50);
        result[0].Alpha.Should().Be(1.0);
    }

    [Fact]
    public void MapToScreen_WithScreenOffset_AddsOffsetToCoordinates()
    {
        var grid = _encoder.BuildGrid(100, 100, 1);
        var painted = _encoder.PaintCells(grid, new Dictionary<string, double> { ["L0.2"] = 0.5 });
        var result = _mapper.MapToScreen(painted, 200, 300);

        result.Should().HaveCount(1);
        result[0].ScreenX.Should().Be(200);
        result[0].ScreenY.Should().Be(300);
        result[0].Alpha.Should().Be(0.5);
    }

    [Fact]
    public void MapToScreen_MultipleVisibleCells_ReturnsAllVisibleInGridOrder()
    {
        var grid = _encoder.BuildGrid(100, 100, 1);
        var painted = _encoder.PaintCells(grid, new Dictionary<string, double>
        {
            ["L0.0"] = 1.0,
            ["L0.1"] = 0.8,
        });
        var result = _mapper.MapToScreen(painted, 0, 0);

        result.Should().HaveCount(2);
        result.Select(r => r.CellCode).Should().Equal("L0.0", "L0.1");
    }

    [Fact]
    public void MapToScreen_AlphaZero_IsVisible()
    {
        var grid = _encoder.BuildGrid(100, 100, 0);
        var painted = _encoder.PaintCells(grid, new Dictionary<string, double> { ["L0"] = 0.0 });
        var result = _mapper.MapToScreen(painted, 0, 0);

        result.Should().HaveCount(1);
        result[0].Alpha.Should().Be(0.0);
    }

    [Fact]
    public void MapToScreen_NullGrid_ThrowsArgumentNullException()
    {
        var act = () => _mapper.MapToScreen(null!, 0, 0);
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void MapToScreen_PreservesCellDimensionsAndOffset()
    {
        var grid = _encoder.BuildGrid(200, 100, 1);
        var painted = _encoder.PaintCells(grid, new Dictionary<string, double> { ["L0.3"] = 1.0 });
        var result = _mapper.MapToScreen(painted, 50, 60);

        result.Should().HaveCount(1);
        result[0].Width.Should().Be(100);
        result[0].Height.Should().Be(50);
        result[0].ScreenX.Should().Be(150);
        result[0].ScreenY.Should().Be(60);
    }
}
