namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// AC-08 小按钮夹逼 E2E — 1920×1080 → 64×64 约需 6 层 zoom 收敛
/// 验证夹逼核心场景：反复 zoom 直到 IsClearEnough=true
/// </summary>
[Trait("Category", "Integration")]
public sealed class DesktopSceneSmallButtonE2ETests
{
    /// <summary>AC-08: 1920×1080 反复 zoom 直到 IsClearEnough，总次数 ≤ 7</summary>
    [Fact]
    public async Task RepeatedZoom_ConvergesToClearEnough_Within7Steps()
    {
        var env = DesktopEnvironmentGuard.CheckInteractiveDesktop();
        env.IsInteractive.Should().BeTrue($"当前环境应为交互式桌面: {env.Diagnostic}");

        var fileSystem = new PhysicalFileSystem();
        var screenCapture = new GdiScreenCaptureService();
        var annotator = new QuadtreeEncoder();
        var renderer = new QuadtreeRenderer(annotator);
        var stateStore = new DesktopSceneStateStore(fileSystem);
        var captureService = new DesktopSceneCaptureService(screenCapture, annotator, renderer, stateStore, fileSystem);
        var zoomService = new DesktopSceneZoomService(renderer, stateStore, fileSystem);

        var sceneId = $"e2e_small_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}";
        var capture = await captureService.CaptureWithGridAsync(sceneId, 2);

        var zoomCount = 0;
        var maxZoom = 10;
        var isClear = false;
        var lastRegionWidth = capture.ImageWidth;
        var lastRegionHeight = capture.ImageHeight;

        while (!isClear && zoomCount < maxZoom)
        {
            var zoom = await zoomService.ZoomAsync(sceneId, 1);
            zoomCount++;
            zoom.RegionWidth.Should().BeLessThanOrEqualTo(lastRegionWidth,
                $"zoom {zoomCount} 后区域宽度应 ≤ 上一次 ({lastRegionWidth})");
            zoom.RegionHeight.Should().BeLessThanOrEqualTo(lastRegionHeight,
                $"zoom {zoomCount} 后区域高度应 ≤ 上一次 ({lastRegionHeight})");
            lastRegionWidth = zoom.RegionWidth;
            lastRegionHeight = zoom.RegionHeight;
            isClear = zoom.IsClearEnough;
        }

        isClear.Should().BeTrue($"应在 {maxZoom} 次 zoom 内收敛到可识别粒度");
        zoomCount.Should().BeLessThanOrEqualTo(7, "1920×1080 → 64×64 约需 6-7 层");
    }

    /// <summary>AC-08: 每次 zoom 区域面积 ≤ 上一次 / 4（四叉树 2×2 分割选 1 块）</summary>
    [Fact]
    public async Task EachZoom_AreaShrinksByAtLeastFactor4()
    {
        var env = DesktopEnvironmentGuard.CheckInteractiveDesktop();
        env.IsInteractive.Should().BeTrue($"当前环境应为交互式桌面: {env.Diagnostic}");

        var fileSystem = new PhysicalFileSystem();
        var screenCapture = new GdiScreenCaptureService();
        var annotator = new QuadtreeEncoder();
        var renderer = new QuadtreeRenderer(annotator);
        var stateStore = new DesktopSceneStateStore(fileSystem);
        var captureService = new DesktopSceneCaptureService(screenCapture, annotator, renderer, stateStore, fileSystem);
        var zoomService = new DesktopSceneZoomService(renderer, stateStore, fileSystem);

        var sceneId = $"e2e_area_{DateTimeOffset.UtcNow:yyyyMMdd_HHmmss}";
        var capture = await captureService.CaptureWithGridAsync(sceneId, 2);
        var previousArea = (long)capture.ImageWidth * capture.ImageHeight;

        for (int i = 1; i <= 5; i++)
        {
            var zoom = await zoomService.ZoomAsync(sceneId, 1);
            var currentArea = (long)zoom.RegionWidth * zoom.RegionHeight;
            currentArea.Should().BeLessThanOrEqualTo(previousArea / 4 + 1,
                $"zoom {i} 后面积应 ≤ 上一次/4 (上一次={previousArea}, 当前={currentArea})");
            previousArea = currentArea;
        }
    }
}
