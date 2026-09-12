namespace Vision.Tests.ToolHandlers;

/// <summary>
/// QuadtreeDesktopOverlayToolHandlers 单元测试 — quadtree_to_screen_rects MCP 工具(ADR 0032 延伸应用)
/// </summary>
public sealed class QuadtreeDesktopOverlayToolHandlersTests
{
    private readonly QuadtreeDesktopOverlayToolHandlers _handlers;

    public QuadtreeDesktopOverlayToolHandlersTests()
    {
        var annotator = new QuadtreeEncoder();
        var mapper = new QuadtreeDesktopOverlayMapper();
        _handlers = new QuadtreeDesktopOverlayToolHandlers(annotator, mapper);
    }

    [Fact]
    public async Task ToScreenRects_ValidInputs_ReturnsScreenRectsJson()
    {
        var result = await _handlers.QuadtreeToScreenRectsAsync(
            imageWidth: 100, imageHeight: 100, depth: 1,
            paintsJson: """{"L0.2":1.0,"L0.3":0.8}""",
            originScreenX: 200, originScreenY: 300);

        result.IsError.Should().BeFalse();
        var text = result.Content[0].Text!;
        using var doc = JsonDocument.Parse(text);
        doc.RootElement.ValueKind.Should().Be(JsonValueKind.Array);
        doc.RootElement.GetArrayLength().Should().Be(2);

        var first = doc.RootElement[0];
        first.GetProperty("cellCode").GetString().Should().Be("L0.2");
        first.GetProperty("screenX").GetInt32().Should().Be(200);
        first.GetProperty("screenY").GetInt32().Should().Be(300);
        first.GetProperty("width").GetInt32().Should().Be(50);
        first.GetProperty("height").GetInt32().Should().Be(50);
        first.GetProperty("alpha").GetDouble().Should().Be(1.0);

        var second = doc.RootElement[1];
        second.GetProperty("cellCode").GetString().Should().Be("L0.3");
        second.GetProperty("screenX").GetInt32().Should().Be(250);
        second.GetProperty("screenY").GetInt32().Should().Be(300);
    }

    [Fact]
    public async Task ToScreenRects_NoPaintsJson_ReturnsAllCellsVisible()
    {
        var result = await _handlers.QuadtreeToScreenRectsAsync(
            imageWidth: 100, imageHeight: 100, depth: 1,
            paintsJson: null,
            originScreenX: 0, originScreenY: 0);

        result.IsError.Should().BeFalse();
        var text = result.Content[0].Text!;
        using var doc = JsonDocument.Parse(text);
        doc.RootElement.GetArrayLength().Should().Be(4);
    }

    [Fact]
    public async Task ToScreenRects_InvalidDimensions_ReturnsError()
    {
        var result = await _handlers.QuadtreeToScreenRectsAsync(
            imageWidth: 0, imageHeight: 100, depth: 1,
            paintsJson: null, originScreenX: 0, originScreenY: 0);
        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("VIS160");
    }

    [Fact]
    public async Task ToScreenRects_InvalidPaintsJson_ReturnsError()
    {
        var result = await _handlers.QuadtreeToScreenRectsAsync(
            imageWidth: 100, imageHeight: 100, depth: 1,
            paintsJson: "not-json",
            originScreenX: 0, originScreenY: 0);
        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("VIS161");
    }

    [Fact]
    public async Task ToScreenRects_NegativeDepth_ReturnsError()
    {
        var result = await _handlers.QuadtreeToScreenRectsAsync(
            imageWidth: 100, imageHeight: 100, depth: -1,
            paintsJson: null, originScreenX: 0, originScreenY: 0);
        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("VIS162");
    }

    [Fact]
    public async Task ToScreenRects_WithOffset_AddsOffsetToCoordinates()
    {
        var result = await _handlers.QuadtreeToScreenRectsAsync(
            imageWidth: 100, imageHeight: 100, depth: 0,
            paintsJson: """{"L0":1.0}""",
            originScreenX: 500, originScreenY: 600);

        result.IsError.Should().BeFalse();
        var text = result.Content[0].Text!;
        using var doc = JsonDocument.Parse(text);
        doc.RootElement.GetArrayLength().Should().Be(1);
        doc.RootElement[0].GetProperty("screenX").GetInt32().Should().Be(500);
        doc.RootElement[0].GetProperty("screenY").GetInt32().Should().Be(600);
    }
}
