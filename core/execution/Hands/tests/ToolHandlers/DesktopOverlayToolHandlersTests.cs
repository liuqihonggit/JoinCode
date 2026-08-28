namespace Hands.Tests.ToolHandlers;

/// <summary>
/// DesktopOverlayToolHandlers 单元测试 — 验证 show_desktop_overlay / show_desktop_pulse 参数校验
/// </summary>
public sealed class DesktopOverlayToolHandlersTests
{
    private readonly DesktopOverlayToolHandlers _handlers = new();

    [Fact]
    public async Task ShowDesktopOverlay_InvalidDimensions_ShouldReturnError()
    {
        var result = await _handlers.ShowDesktopOverlayAsync(0, 0, 0, 100);
        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("[OVL100]");
    }

    [Fact]
    public async Task ShowDesktopOverlay_InvalidDuration_ShouldReturnError()
    {
        var result = await _handlers.ShowDesktopOverlayAsync(0, 0, 100, 100, durationMs: 0);
        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("[OVL101]");
    }

    [Fact]
    public async Task ShowDesktopPulse_InvalidRadius_ShouldReturnError()
    {
        var result = await _handlers.ShowDesktopPulseAsync(500, 500, maxRadius: 0);
        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("[OVL200]");
    }

    [Fact]
    public async Task ShowDesktopPulse_MinRadiusNotLessThanMax_ShouldReturnError()
    {
        var result = await _handlers.ShowDesktopPulseAsync(500, 500, maxRadius: 50, minRadius: 50);
        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("[OVL200]");
    }

    [Fact]
    public async Task ShowDesktopPulse_InvalidDuration_ShouldReturnError()
    {
        var result = await _handlers.ShowDesktopPulseAsync(500, 500, durationMs: 0);
        result.IsError.Should().BeTrue();
        result.Content[0].Text.Should().Contain("[OVL200]");
    }
}
