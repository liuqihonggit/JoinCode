namespace JoinCode.Vision.Tests;

/// <summary>
/// 四叉树编码器单元测试 — 编码/坐标/方位/邻居/染色
/// </summary>
public sealed class QuadtreeEncoderTests
{
    private readonly QuadtreeEncoder _encoder = new();

    [Fact]
    public void BuildGrid_Depth0_ReturnsSingleRootCell()
    {
        var grid = _encoder.BuildGrid(100, 100, 0);

        grid.Cells.Should().HaveCount(1);
        grid.Cells[0].Code.Should().Be("L0");
        grid.Cells[0].X.Should().Be(0);
        grid.Cells[0].Y.Should().Be(0);
        grid.Cells[0].Width.Should().Be(100);
        grid.Cells[0].Height.Should().Be(100);
        grid.Depth.Should().Be(0);
    }

    [Fact]
    public void BuildGrid_Depth1_Returns4Cells()
    {
        var grid = _encoder.BuildGrid(100, 100, 1);
        grid.Cells.Should().HaveCount(4);
    }

    [Fact]
    public void BuildGrid_Depth2_Returns16Cells()
    {
        var grid = _encoder.BuildGrid(100, 100, 2);
        grid.Cells.Should().HaveCount(16);
    }

    [Fact]
    public void BuildGrid_Depth1_CodesAreL0_0_L0_1_L0_2_L0_3()
    {
        var grid = _encoder.BuildGrid(100, 100, 1);
        var codes = grid.Cells.Select(c => c.Code).OrderBy(c => c).ToList();
        codes.Should().Equal("L0.0", "L0.1", "L0.2", "L0.3");
    }

    [Fact]
    public void BuildGrid_Depth1_CoordinatesMatchQuadrantSemantics()
    {
        var grid = _encoder.BuildGrid(100, 100, 1);
        var byCode = grid.Cells.ToDictionary(c => c.Code);

        var nw = byCode["L0.2"];
        nw.X.Should().Be(0);
        nw.Y.Should().Be(0);
        nw.Quadrant.Should().Be(Quadrant.NW);

        var ne = byCode["L0.3"];
        ne.X.Should().Be(50);
        ne.Y.Should().Be(0);
        ne.Quadrant.Should().Be(Quadrant.NE);

        var sw = byCode["L0.0"];
        sw.X.Should().Be(0);
        sw.Y.Should().Be(50);
        sw.Quadrant.Should().Be(Quadrant.SW);

        var se = byCode["L0.1"];
        se.X.Should().Be(50);
        se.Y.Should().Be(50);
        se.Quadrant.Should().Be(Quadrant.SE);
    }

    [Fact]
    public void BuildGrid_AllCellsAlphaMinus1()
    {
        var grid = _encoder.BuildGrid(100, 100, 2);
        grid.Cells.Should().AllSatisfy(c => c.Alpha.Should().Be(-1));
    }

    [Fact]
    public void BuildGrid_Depth1_RegionCorrect()
    {
        var grid = _encoder.BuildGrid(100, 100, 1);
        var byCode = grid.Cells.ToDictionary(c => c.Code);

        byCode["L0.2"].Region.Should().Be("左上");
        byCode["L0.3"].Region.Should().Be("右上");
        byCode["L0.0"].Region.Should().Be("左下");
        byCode["L0.1"].Region.Should().Be("右下");
    }

    [Fact]
    public void GetNeighbor_East_ReturnsEastNeighbor()
    {
        var neighbor = _encoder.GetNeighbor("L0.2", CardinalDirection.E, 100, 100, 1);
        neighbor.Should().Be("L0.3");
    }

    [Fact]
    public void GetNeighbor_South_ReturnsSouthNeighbor()
    {
        var neighbor = _encoder.GetNeighbor("L0.2", CardinalDirection.S, 100, 100, 1);
        neighbor.Should().Be("L0.0");
    }

    [Fact]
    public void GetNeighbor_NorthAtBoundary_ReturnsNull()
    {
        var neighbor = _encoder.GetNeighbor("L0.2", CardinalDirection.N, 100, 100, 1);
        neighbor.Should().BeNull();
    }

    [Fact]
    public void GetNeighbor_WestAtBoundary_ReturnsNull()
    {
        var neighbor = _encoder.GetNeighbor("L0.2", CardinalDirection.W, 100, 100, 1);
        neighbor.Should().BeNull();
    }

    [Fact]
    public void GetNeighbor_DiagonalSE_ReturnsDiagonalNeighbor()
    {
        var neighbor = _encoder.GetNeighbor("L0.2", CardinalDirection.SE, 100, 100, 1);
        neighbor.Should().Be("L0.1");
    }

    [Fact]
    public void GetNeighbor_Depth2_TraversesCorrectly()
    {
        var neighbor = _encoder.GetNeighbor("L0.2.1", CardinalDirection.E, 100, 100, 2);
        neighbor.Should().NotBeNull();
        var (col, row) = QuadtreeEncoder.DecodeToGrid(neighbor!);
        var (origCol, origRow) = QuadtreeEncoder.DecodeToGrid("L0.2.1");
        col.Should().Be(origCol + 1);
        row.Should().Be(origRow);
    }

    [Fact]
    public void PaintCells_UpdatesAlphaForSpecifiedCells()
    {
        var grid = _encoder.BuildGrid(100, 100, 1);
        var paints = new Dictionary<string, double> { ["L0.2"] = 0.8 };
        var painted = _encoder.PaintCells(grid, paints);

        var byCode = painted.Cells.ToDictionary(c => c.Code);
        byCode["L0.2"].Alpha.Should().Be(0.8);
        byCode["L0.3"].Alpha.Should().Be(-1);
        byCode["L0.0"].Alpha.Should().Be(-1);
        byCode["L0.1"].Alpha.Should().Be(-1);
    }

    [Fact]
    public void PaintCells_EmptyPaints_ReturnsSameInstance()
    {
        var grid = _encoder.BuildGrid(100, 100, 1);
        var painted = _encoder.PaintCells(grid, new Dictionary<string, double>());
        painted.Should().BeSameAs(grid);
    }

    [Fact]
    public void PaintCells_MultipleCells_UpdatesAll()
    {
        var grid = _encoder.BuildGrid(100, 100, 1);
        var paints = new Dictionary<string, double> { ["L0.2"] = 0.5, ["L0.1"] = 1.0 };
        var painted = _encoder.PaintCells(grid, paints);
        var byCode = painted.Cells.ToDictionary(c => c.Code);
        byCode["L0.2"].Alpha.Should().Be(0.5);
        byCode["L0.1"].Alpha.Should().Be(1.0);
    }

    [Theory]
    [InlineData(0, 0, 1, "L0.2")]
    [InlineData(1, 0, 1, "L0.3")]
    [InlineData(0, 1, 1, "L0.0")]
    [InlineData(1, 1, 1, "L0.1")]
    public void EncodeFromGrid_Depth1_MapsColRowToCode(int col, int row, int depth, string expected)
    {
        QuadtreeEncoder.EncodeFromGrid(col, row, depth).Should().Be(expected);
    }

    [Theory]
    [InlineData("L0.2", 0, 0)]
    [InlineData("L0.3", 1, 0)]
    [InlineData("L0.0", 0, 1)]
    [InlineData("L0.1", 1, 1)]
    public void DecodeToGrid_Depth1_MapsCodeToColRow(string code, int expectedCol, int expectedRow)
    {
        var (col, row) = QuadtreeEncoder.DecodeToGrid(code);
        col.Should().Be(expectedCol);
        row.Should().Be(expectedRow);
    }

    [Fact]
    public void EncodeDecode_RoundTrip_Depth2()
    {
        for (var col = 0; col < 4; col++)
        {
            for (var row = 0; row < 4; row++)
            {
                var code = QuadtreeEncoder.EncodeFromGrid(col, row, 2);
                var (decodedCol, decodedRow) = QuadtreeEncoder.DecodeToGrid(code);
                decodedCol.Should().Be(col);
                decodedRow.Should().Be(row);
            }
        }
    }

    [Theory]
    [InlineData(0, 100)]
    [InlineData(100, 0)]
    [InlineData(-1, 100)]
    public void BuildGrid_InvalidDimensions_Throws(int width, int height)
    {
        var act = () => _encoder.BuildGrid(width, height, 1);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void BuildGrid_NegativeDepth_Throws()
    {
        var act = () => _encoder.BuildGrid(100, 100, -1);
        act.Should().Throw<ArgumentException>();
    }
}
