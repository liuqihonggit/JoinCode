namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// AC-10 渐进式披露 Token 效率验证 — menu JSON ≤ 500 token, suggested_next ≤ 100 token
/// </summary>
public sealed class DesktopSceneTokenBudgetTests
{
    private static int EstimateTokens(string text) => text.Length / 4;

    /// <summary>AC-10: menu 返回的 JSON ≤ 500 token</summary>
    [Fact]
    public async Task SceneMenu_ReturnsJsonUnder500Tokens()
    {
        var handler = new DesktopSceneMenuToolHandlers();
        var result = await handler.SceneMenuAsync();
        var text = result.GetFirstText()!;

        var tokens = EstimateTokens(text);
        tokens.Should().BeLessThanOrEqualTo(500, $"menu JSON 应 ≤ 500 token, 实际 {tokens} token ({text.Length} 字符)");
    }

    /// <summary>AC-10: look 返回的 suggested_next ≤ 100 token</summary>
    [Fact]
    public async Task Look_SuggestedNextUnder100Tokens()
    {
        var captureMock = new Mock<IDesktopSceneCaptureService>();
        captureMock.Setup(c => c.CaptureWithGridAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DesktopSceneCapture("fake", "fake", 100, 100, 2));
        var handler = new DesktopSceneLookToolHandlers(captureMock.Object);

        var result = await handler.LookAsync("sc_token_test");
        var text = result.GetFirstText()!;

        var suggestedNextStart = text.IndexOf("\"suggested_next\"");
        suggestedNextStart.Should().BeGreaterThan(-1, "应含 suggested_next 字段");
        var suggestedNextJson = text[suggestedNextStart..];
        var tokens = EstimateTokens(suggestedNextJson);
        tokens.Should().BeLessThanOrEqualTo(100, $"suggested_next 应 ≤ 100 token, 实际 {tokens} token");
    }

    /// <summary>AC-10: zoom 返回的 suggested_next ≤ 100 token</summary>
    [Fact]
    public async Task Zoom_SuggestedNextUnder100Tokens()
    {
        var zoomMock = new Mock<IDesktopSceneZoomService>();
        zoomMock.Setup(z => z.ZoomAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DesktopSceneZoom("fake", "L0.2", 1, 960, 540, false));
        var handler = new DesktopSceneZoomToolHandlers(zoomMock.Object);

        var result = await handler.ZoomAsync("sc_test", 2);
        var text = result.GetFirstText()!;

        var suggestedNextStart = text.IndexOf("\"suggested_next\"");
        var suggestedNextJson = text[suggestedNextStart..];
        var tokens = EstimateTokens(suggestedNextJson);
        tokens.Should().BeLessThanOrEqualTo(100, $"suggested_next 应 ≤ 100 token, 实际 {tokens} token");
    }
}
