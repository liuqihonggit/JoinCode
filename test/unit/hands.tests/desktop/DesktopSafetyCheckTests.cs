namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// AC-12 危险坐标拦截验证 — 安全检查器返回 DangerousCoordinate 时点击被拦截不执行 SendInput
/// </summary>
[Trait("Category", "Integration")]
public sealed class DesktopSafetyCheckTests {
    /// <summary>AC-12: safetyChecker 返回 DangerousCoordinate 时 ClickAsync 返回失败且不执行 SendInput</summary>
    [Fact]
    public async Task ClickAsync_DangerousCoordinate_ReturnsFailureAndDoesNotExecute() {
        var env = DesktopEnvironmentGuard.CheckInteractiveDesktop();
        env.IsInteractive.Should().BeTrue($"当前环境应为交互式桌面: {env.Diagnostic}");

        var safetyMock = new Mock<IDesktopSafetyChecker>();
        safetyMock.Setup(s => s.CheckClickAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UnsafeOperationKind.DangerousCoordinate);
        await using var inputService = new Win32DesktopInputService(safetyMock.Object);

        var result = await inputService.ClickAsync(100, 200, MouseAction.Click);

        result.Succeeded.Should().BeFalse("危险坐标应被拦截");
        result.Error.Should().Be("DangerousCoordinate", "错误应标明危险坐标拦截");
        result.Kind.Should().Be(DesktopOperationKind.Click, "操作类型应为 Click");
        result.X.Should().Be(100);
        result.Y.Should().Be(200);
    }

    /// <summary>AC-12: safetyChecker 返回 None 时 ClickAsync 正常执行</summary>
    [Fact]
    public async Task ClickAsync_SafeCoordinate_ExecutesSuccessfully() {
        var env = DesktopEnvironmentGuard.CheckInteractiveDesktop();
        env.IsInteractive.Should().BeTrue($"当前环境应为交互式桌面: {env.Diagnostic}");

        var safetyMock = new Mock<IDesktopSafetyChecker>();
        safetyMock.Setup(s => s.CheckClickAsync(It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(UnsafeOperationKind.None);
        await using var inputService = new Win32DesktopInputService(safetyMock.Object);

        var result = await inputService.ClickAsync(500, 500, MouseAction.Move);

        result.Succeeded.Should().BeTrue("安全坐标应正常执行");
        result.Error.Should().BeNull();
    }
}