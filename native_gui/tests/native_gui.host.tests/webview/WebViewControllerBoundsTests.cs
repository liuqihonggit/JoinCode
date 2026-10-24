namespace JoinCode.Gui.Tests.WebView;

/// <summary>
/// WebView2 控制器尺寸换算测试（缩窗后最大化露黑区回归，2026-10-25）：
/// 控制器物理像素尺寸必须由每次排布传入的尺寸直接换算。Avalonia 在 <c>ArrangeOverride</c>
/// 返回后才赋值 <c>Bounds</c>，若换算读 <c>Bounds</c> 就会慢一拍（原生子窗口已是新尺寸，
/// 控制器仍停在旧尺寸，露出未绘制黑区）。本测试锁死"每次排布用本次尺寸"这一契约。
/// </summary>
public class WebViewControllerBoundsTests {
    [Fact]
    public void Resolve_ScalesDipToPhysicalPixels() {
        WebView2Host.ResolveControllerBounds(new Size(800, 600), 1.5).Should().Be((1200, 900));
    }

    [Fact]
    public void Resolve_IdentityScaling_KeepsDipSize() {
        WebView2Host.ResolveControllerBounds(new Size(1440, 900), 1.0).Should().Be((1440, 900));
    }

    /// <summary>连续缩放（初始 → 缩小 → 最大化）每步都必须取本次尺寸，禁止沿用上一次结果。</summary>
    [Fact]
    public void Resolve_TracksEveryResizeStep() {
        WebView2Host.ResolveControllerBounds(new Size(916, 405), 1.0).Should().Be((916, 405));
        WebView2Host.ResolveControllerBounds(new Size(884, 601), 1.0).Should().Be((884, 601));
        WebView2Host.ResolveControllerBounds(new Size(1920, 1017), 1.0).Should().Be((1920, 1017));
    }

    [Fact]
    public void Resolve_RoundsFractionalPixels() {
        WebView2Host.ResolveControllerBounds(new Size(884.4, 601.6), 1.0).Should().Be((884, 602));
    }

    [Theory]
    [InlineData(0, 600)]
    [InlineData(800, 0)]
    [InlineData(-1, 600)]
    [InlineData(800, -1)]
    public void Resolve_NonPositiveSize_ReturnsNull(double width, double height) {
        WebView2Host.ResolveControllerBounds(new Size(width, height), 1.0).Should().BeNull();
    }

    /// <summary>
    /// 回归（2026-10-25，125% 缩放下控制器按 DIP 尺寸绘制露出空白带）：
    /// Avalonia 12 的 <c>VisualRoot</c> 是 TopLevelHost，<c>as TopLevel</c> 恒为 null，
    /// 缩放退回 1.0。必须经 <c>TopLevel.GetTopLevel</c> 找宿主窗口。
    /// </summary>
    [AvaloniaFact]
    public void ResolveRenderScaling_FindsTopLevelThroughVisualRootHost() {
        var window = new Window { Width = 300, Height = 200 };
        var probe = new Control();
        window.Content = probe;
        window.Show();
        try {
            TopLevel.GetTopLevel(probe).Should().BeSameAs(window);
            WebView2Host.ResolveRenderScaling(probe).Should().Be(window.RenderScaling);
        } finally {
            window.Close();
        }
    }

    [Fact]
    public void ResolveRenderScaling_DetachedVisual_FallsBackToOne() {
        WebView2Host.ResolveRenderScaling(new Control()).Should().Be(1.0);
    }
}
