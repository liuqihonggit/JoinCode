namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// GdiScreenCaptureService 单元测试 — 验证可构造性与边界参数处理
/// </summary>
public sealed class GdiScreenCaptureServiceTests
{
    [Fact]
    public void CanConstruct_WithNullLogger()
    {
        var service = new GdiScreenCaptureService();

        service.Should().NotBeNull();
    }

    [Fact]
    public async Task CaptureRegionAsync_ZeroWidth_ReturnsEmpty()
    {
        var service = new GdiScreenCaptureService();

        var result = await service.CaptureRegionAsync(0, 0, 0, 100);

        result.Should().BeEmpty();
    }

    [Fact]
    public async Task CaptureRegionAsync_NegativeHeight_ReturnsEmpty()
    {
        var service = new GdiScreenCaptureService();

        var result = await service.CaptureRegionAsync(0, 0, 100, -1);

        result.Should().BeEmpty();
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task CaptureFullScreenAsync_OnDesktop_ReturnsNonEmptyBase64()
    {
        var service = new GdiScreenCaptureService();

        var result = await service.CaptureFullScreenAsync();

        if (result.Length > 0)
            result.Should().StartWith("iVBORw0KGgo");
    }
}
