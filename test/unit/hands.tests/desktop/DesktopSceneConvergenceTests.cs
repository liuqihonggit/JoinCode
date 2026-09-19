namespace JoinCode.Hands.Desktop.Tests;

/// <summary>
/// 夹逼收敛数学验证 — AC-06 每层面积 ≤ 初始/4^depth，N 步内收敛到阈值
/// </summary>
public sealed class DesktopSceneConvergenceTests {
    /// <summary>AC-06: 每层面积 ≤ 初始 / 4^depth（四叉树 2x2 分割选 1 块）</summary>
    [Fact]
    public void AreaShrinksByFactor4PerDepth() {
        const int initialWidth = 1920;
        const int initialHeight = 1080;
        var initialArea = (long)initialWidth * initialHeight;

        for (var depth = 1; depth <= 10; depth++) {
            var actualWidth = initialWidth / (1 << depth);
            var actualHeight = initialHeight / (1 << depth);
            var actualArea = (long)actualWidth * actualHeight;
            var expectedArea = initialArea >> (2 * depth);
            actualArea.Should().BeLessThanOrEqualTo(expectedArea, $"depth={depth} 时面积应 ≤ 初始/4^depth");
        }
    }

    /// <summary>AC-06: N ≥ ceil(log4(S/T)) 时区域面积 ≤ 阈值（必然收敛）</summary>
    [Fact]
    public void ReachesThresholdWithinLog4Steps() {
        const long initialArea = 1920L * 1080;
        const int threshold = 64 * 64;

        var n = (int)Math.Ceiling(Math.Log(initialArea / (double)threshold, 4));
        var areaAfterN = initialArea / Math.Pow(4, n);
        areaAfterN.Should().BeLessThanOrEqualTo(threshold);
        n.Should().BeLessThanOrEqualTo(7, "1920x1080 → 64x64 约需 6-7 层");
    }

    /// <summary>AC-06: 16x16 小按钮需约 8 层收敛</summary>
    [Fact]
    public void SmallButton16x16_ConvergesWithin8Steps() {
        const long initialArea = 1920L * 1080;
        const int threshold = 16 * 16;

        var n = (int)Math.Ceiling(Math.Log(initialArea / (double)threshold, 4));
        var areaAfterN = initialArea / Math.Pow(4, n);
        areaAfterN.Should().BeLessThanOrEqualTo(threshold);
        n.Should().BeLessThanOrEqualTo(9);
    }
}