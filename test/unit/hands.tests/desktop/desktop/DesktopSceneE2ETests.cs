namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// AC-07 E2E 真实运行冒烟测试 — 真实截图 + 四叉树网格 + 渲染 + 状态持久化
/// 标记 Integration 类别，CI 跳过，本地真实桌面运行
/// </summary>
[Trait("Category", "Integration")]
public sealed class DesktopSceneE2ETests {
    /// <summary>AC-07: 真实调 desktop_look 截图建网格，验证截图非空 + 尺寸正确 + 状态文件生成</summary>
    [Fact]
    public async Task Look_RealDesktop_ReturnsScreenshotAndPersistsState() {
        var env = DesktopEnvironmentGuard.CheckInteractiveDesktop();
        env.IsInteractive.Should().BeTrue($"当前环境应为交互式桌面: {env.Diagnostic}");

        await using var fileSystem = new PhysicalFileSystem();
        await using var screenCapture = new GdiScreenCaptureService();
        var annotator = new QuadtreeEncoder();
        var renderer = new QuadtreeRenderer(annotator);
        await using var stateStore = new DesktopSceneStateStore(fileSystem);
        await using var captureService = new DesktopSceneCaptureService(screenCapture, annotator, renderer, stateStore, fileSystem);

        var sceneId = $"e2e_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}";
        var capture = await captureService.CaptureWithGridAsync(sceneId, 2);

        capture.ScreenshotBase64.Should().NotBeNullOrEmpty("截图 base64 应非空");
        capture.RenderedBase64.Should().NotBeNullOrEmpty("渲染图 base64 应非空");
        capture.ImageWidth.Should().BeGreaterThan(0, "图片宽度应 > 0");
        capture.ImageHeight.Should().BeGreaterThan(0, "图片高度应 > 0");
        capture.Depth.Should().Be(2);

        var state = await stateStore.LoadAsync(sceneId);
        state.Should().NotBeNull("状态应已持久化");
        state!.CurrentDepth.Should().Be(0, "初始层数为 0");
        state.CurrentCellCode.Should().Be("L0", "初始格子编码为 L0");
        state.LastScreenshotPath.Should().NotBeNullOrEmpty("截图路径应已记录");
        fileSystem.FileExists(state.LastScreenshotPath!).Should().BeTrue("截图文件应存在");

        var screenshotContent = await fileSystem.ReadAllTextAsync(state.LastScreenshotPath!);
        screenshotContent.Should().NotBeNullOrEmpty("截图文件内容应非空");
        screenshotContent.Should().Be(capture.ScreenshotBase64, "文件内容应与返回的 base64 一致");
    }

    /// <summary>AC-07: 真实调 look → zoom 链路，验证缩放后子图非空 + 状态更新</summary>
    [Fact]
    public async Task LookThenZoom_RealDesktop_ReturnsSubImageAndUpdatesState() {
        var env = DesktopEnvironmentGuard.CheckInteractiveDesktop();
        env.IsInteractive.Should().BeTrue($"当前环境应为交互式桌面: {env.Diagnostic}");

        await using var fileSystem = new PhysicalFileSystem();
        await using var screenCapture = new GdiScreenCaptureService();
        var annotator = new QuadtreeEncoder();
        var renderer = new QuadtreeRenderer(annotator);
        await using var stateStore = new DesktopSceneStateStore(fileSystem);
        await using var captureService = new DesktopSceneCaptureService(screenCapture, annotator, renderer, stateStore, fileSystem);
        await using var zoomService = new DesktopSceneZoomService(renderer, stateStore, fileSystem);

        var sceneId = $"e2e_zoom_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}";
        await captureService.CaptureWithGridAsync(sceneId, 2);

        var zoom = await zoomService.ZoomAsync(sceneId, 1);

        zoom.SubImageBase64.Should().NotBeNullOrEmpty("缩放后子图应非空");
        zoom.CurrentDepth.Should().Be(1, "zoom 一次后层数为 1");
        zoom.CurrentCellCode.Should().StartWith("L0.", "格子编码应扩展");
        zoom.RegionWidth.Should().BeGreaterThan(0, "区域宽度应 > 0");
        zoom.RegionHeight.Should().BeGreaterThan(0, "区域高度应 > 0");

        var state = await stateStore.LoadAsync(sceneId);
        state!.CurrentDepth.Should().Be(1, "状态层数应更新为 1");
        state.ZoomHistory.Should().HaveCount(1, "zoom 历史应有 1 条");
        state.LastScreenshotPath.Should().NotBeNullOrEmpty("截图路径应更新为子图路径");
        fileSystem.FileExists(state.LastScreenshotPath!).Should().BeTrue("子图文件应存在");
    }

    /// <summary>AC-09: zoom 3 层后 back 1 层，断言 depth=2 + cellCode 回退 + history 删最后一条</summary>
    [Fact]
    public async Task ZoomThreeLayersThenBack_DepthDecreasesAndHistoryShrinks() {
        var env = DesktopEnvironmentGuard.CheckInteractiveDesktop();
        env.IsInteractive.Should().BeTrue($"当前环境应为交互式桌面: {env.Diagnostic}");

        await using var fileSystem = new PhysicalFileSystem();
        await using var screenCapture = new GdiScreenCaptureService();
        var annotator = new QuadtreeEncoder();
        var renderer = new QuadtreeRenderer(annotator);
        await using var stateStore = new DesktopSceneStateStore(fileSystem);
        await using var captureService = new DesktopSceneCaptureService(screenCapture, annotator, renderer, stateStore, fileSystem);
        await using var zoomService = new DesktopSceneZoomService(renderer, stateStore, fileSystem);

        var sceneId = $"e2e_back_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}";
        await captureService.CaptureWithGridAsync(sceneId, 2);
        await zoomService.ZoomAsync(sceneId, 1);
        await zoomService.ZoomAsync(sceneId, 2);
        await zoomService.ZoomAsync(sceneId, 3);

        var stateBeforeBack = await stateStore.LoadAsync(sceneId);
        stateBeforeBack!.CurrentDepth.Should().Be(3, "zoom 3 次后层数为 3");
        stateBeforeBack.ZoomHistory.Should().HaveCount(3, "zoom 历史应有 3 条");

        var zoomBack = await zoomService.ZoomAsync(sceneId, 0, back: true);

        zoomBack.CurrentDepth.Should().Be(2, "back 1 层后层数为 2");
        zoomBack.CurrentCellCode.Count(c => c == '.').Should().Be(2, "第 2 层 cellCode 应有 2 个点 (L0.x.y)");
        zoomBack.SubImageBase64.Should().NotBeNullOrEmpty("退回后子图应非空");

        var stateAfterBack = await stateStore.LoadAsync(sceneId);
        stateAfterBack!.CurrentDepth.Should().Be(2, "状态层数应为 2");
        stateAfterBack.ZoomHistory.Should().HaveCount(2, "zoom 历史应删至 2 条");
        stateAfterBack.LastAction.Should().Be("zoom_back", "最后动作应为 zoom_back");
    }
}