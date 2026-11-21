namespace NativeTui.Tests.Repl;

/// <summary>
/// TASK069-P4 — 解释区三态渲染：加载行（点动画由 50ms 时钟推进）/ 不可用行 / 就绪三段
/// （做什么 · 为什么 · 风险等级+一句话），以及高度与渲染行数一致（行数漂移会把问题行顶出对话框）。
/// </summary>
public sealed class PermissionRequestItemExplanationTests
{
    /// <summary>构造挂起请求的协调器（按需装配解释状态）。</summary>
    private static PermissionCoordinator BeginCoordinator(bool enabled = true)
    {
        var coordinator = new PermissionCoordinator { ExplanationEnabled = enabled };
        coordinator.Begin(new PermissionRequest("WebFetch", "是否允许访问？", string.Empty));
        return coordinator;
    }

    private static readonly PermissionExplanationView Sample =
        new("抓取该页面正文", "我需要页面内容来回答问题", "只读网络请求，不会改动本地文件", PermissionRiskBand.Low);

    /// <summary>渲染到独立 Screen（60 列 — 留足宽度免折行影响行数断言）。</summary>
    private static Screen Render(PermissionCoordinator coordinator, int rows, long animTimeMs = 0)
    {
        var item = new PermissionRequestItem(coordinator) { AnimTimeMs = animTimeMs };
        var screen = new Screen(cols: 60, rows: rows);
        item.Render(screen, new Rect(0, 0, 60, rows), Theme.Dark);
        return screen;
    }

    // ---- 加载态 ----

    /// <summary>加载态 — 空行 + 「正在生成解释」，高度 = 骨架 12 + 描述 1 + 2。</summary>
    [Fact]
    public void Loading_RendersLoadingLine()
    {
        var coordinator = BeginCoordinator();
        coordinator.BeginExplanationLoading();

        using var screen = Render(coordinator, rows: 15);

        Assert.Equal(15, new PermissionRequestItem(coordinator).MeasureHeight(width: 60));
        Assert.Equal("   正在生成解释", RowText.Of(screen, 7, 0, 60).TrimEnd());
        Assert.Equal(" Do you want to proceed?", RowText.Of(screen, 9, 0, 24));
    }

    /// <summary>加载动画 — 点数随动画时钟推进（0/300/600/900ms → 0/1/2/3 点，1s 循环）。</summary>
    [Theory]
    [InlineData(0L, "   正在生成解释")]
    [InlineData(300L, "   正在生成解释.")]
    [InlineData(600L, "   正在生成解释..")]
    [InlineData(900L, "   正在生成解释...")]
    [InlineData(1200L, "   正在生成解释")]
    public void Loading_DotsFollowAnimationClock(long animTimeMs, string expected)
    {
        var coordinator = BeginCoordinator();
        coordinator.BeginExplanationLoading();

        using var screen = Render(coordinator, rows: 15, animTimeMs: animTimeMs);

        Assert.Equal(expected, RowText.Of(screen, 7, 0, 60).TrimEnd());
    }

    // ---- 不可用态 ----

    /// <summary>不可用 — dim「解释不可用」一行，高度 +2。</summary>
    [Fact]
    public void Unavailable_RendersDimNotice()
    {
        var coordinator = BeginCoordinator();
        coordinator.BeginExplanationLoading();
        coordinator.CompleteExplanation(null);

        using var screen = Render(coordinator, rows: 15);

        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal("   解释不可用", RowText.Of(screen, 7, 0, 60).TrimEnd());
        Assert.Equal(dim, screen[7, 3].StyleId);
    }

    // ---- 就绪态 ----

    /// <summary>就绪三段 — 解释 / 为什么 / 风险等级+一句话，段间各一空行；高度 = 12 + 描述 1 + 6。</summary>
    [Fact]
    public void Ready_RendersThreeSections()
    {
        var coordinator = BeginCoordinator();
        coordinator.BeginExplanationLoading();
        coordinator.CompleteExplanation(Sample);

        var item = new PermissionRequestItem(coordinator);
        using var screen = Render(coordinator, rows: 19);

        Assert.Equal(19, item.MeasureHeight(width: 60));
        Assert.Equal("   抓取该页面正文", RowText.Of(screen, 7, 0, 60).TrimEnd());
        Assert.Equal("   我需要页面内容来回答问题", RowText.Of(screen, 9, 0, 60).TrimEnd());
        Assert.Equal("   低风险: 只读网络请求，不会改动本地文件", RowText.Of(screen, 11, 0, 60).TrimEnd());

        // 三段把问题行挤到 row13，选项随之下移 —— 解释区只插位，不覆盖既有结构
        Assert.Equal(" Do you want to proceed?", RowText.Of(screen, 13, 0, 24));
        Assert.Equal("❯ 1. Yes", RowText.Of(screen, 14, 0, 8));
        Assert.Contains("ctrl+e 收起解释", RowText.Of(screen, 18, 0, 60), StringComparison.Ordinal);
    }

    /// <summary>风险等级三色 — 低=成功色 / 中=警告色 / 高=错误色（首列是标签，必须上色）。</summary>
    [Theory]
    [InlineData(PermissionRiskBand.Low)]
    [InlineData(PermissionRiskBand.Medium)]
    [InlineData(PermissionRiskBand.High)]
    public void Ready_RiskLabelUsesBandColor(PermissionRiskBand band)
    {
        var coordinator = BeginCoordinator();
        coordinator.BeginExplanationLoading();
        coordinator.CompleteExplanation(Sample with { RiskBand = band });

        using var screen = Render(coordinator, rows: 19);

        var expected = screen.Styles.GetOrAdd(new TextStyle {
            Foreground = band switch {
                PermissionRiskBand.Low => Theme.Dark.Success,
                PermissionRiskBand.Medium => Theme.Dark.Warning,
                _ => Theme.Dark.Error,
            },
        });
        Assert.Equal(expected, screen[11, 3].StyleId);
    }

    // ---- 折叠与提示 ----

    /// <summary>未展开 — 高度与 P4 之前逐字一致（零行为变化锚点）。</summary>
    [Fact]
    public void Hidden_DoesNotChangeHeight()
    {
        var coordinator = BeginCoordinator();

        Assert.Equal(13, new PermissionRequestItem(coordinator).MeasureHeight(width: 60));
    }

    /// <summary>提示行随状态变化 — 未展开说「ctrl+e 解释」，展开后说「ctrl+e 收起解释」。</summary>
    [Fact]
    public void EscHint_ReflectsExplanationState()
    {
        var coordinator = BeginCoordinator();
        // 收起态高度 13 → 提示行落在最后一行（row12）
        using (var collapsed = Render(coordinator, rows: 13))
        {
            Assert.Contains("ctrl+e 解释", RowText.Of(collapsed, 12, 0, 60), StringComparison.Ordinal);
        }

        coordinator.BeginExplanationLoading();
        coordinator.CompleteExplanation(Sample);
        using var expanded = Render(coordinator, rows: 19);
        Assert.Contains("ctrl+e 收起解释", RowText.Of(expanded, 18, 0, 60), StringComparison.Ordinal);
    }

    /// <summary>开关关闭 — 提示行不提 ctrl+e（避免出现按了没反应的假入口）。</summary>
    [Fact]
    public void EscHint_WithoutExplainer_OmitsCtrlE()
    {
        var coordinator = BeginCoordinator(enabled: false);

        using var screen = Render(coordinator, rows: 13);

        Assert.DoesNotContain("ctrl+e", RowText.Of(screen, 12, 0, 60), StringComparison.Ordinal);
    }
}
