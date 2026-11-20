namespace NativeTui.Tests.Repl;

/// <summary>
/// TASK069-P5 触发原因区渲染测试 — PermissionRequestItem 消费 <see cref="PermissionRequest.RuleContent"/>：
/// 非空时在形态区之后、问题行之前插入两行（「触发原因: 工具(规则内容)」+ dim「/permissions 更新规则」），
/// 为空时整区折叠且 <see cref="PermissionRequestItem.MeasureHeight"/> 不多算一行（不占位、不留空白）。
/// 数据面唯一来源是 WebFetch 的 <c>domain:hostname</c>；其余确认没有可指名的规则，故不编造。
/// </summary>
public sealed class PermissionRequestItemReasonTests
{
    /// <summary>构造挂起请求的 coordinator（可带触发原因规则内容）。</summary>
    private static PermissionCoordinator BeginCoordinator(string tool, string description, string ruleContent)
    {
        var coordinator = new PermissionCoordinator();
        coordinator.Begin(new PermissionRequest(tool, description, ruleContent));
        return coordinator;
    }

    // ---- 有规则内容 ----

    /// <summary>有规则内容 — 原因两行插在问题行之前，首行默认样式、引导行 dim。</summary>
    [Fact]
    public void Render_WithRuleContent_DrawsReasonRegionBeforeQuestion()
    {
        var coordinator = BeginCoordinator("WebFetch", "是否允许访问？", "domain:example.com");
        using var screen = new Screen(cols: 60, rows: 15);
        new PermissionRequestItem(coordinator).Render(screen, new Rect(0, 0, 60, 15), Theme.Dark);

        // 骨架 row1 边框 / row2 标题 / row4 工具行 / row5 描述 → 原因区占 row6、row7
        Assert.Equal("   触发原因: WebFetch(domain:example.com)", RowText.Of(screen, 6, 0, 60).TrimEnd());
        Assert.Equal(0, screen[6, 3].StyleId);

        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        Assert.Equal("   /permissions 更新规则", RowText.Of(screen, 7, 0, 60).TrimEnd());
        Assert.Equal(dim, screen[7, 3].StyleId);

        // 问题行与选项整体下移两行（原因区不覆盖既有结构，只挤占中间位置）
        Assert.Equal(" Do you want to proceed?", RowText.Of(screen, 9, 0, 24));
        Assert.Equal("❯ 1. Yes", RowText.Of(screen, 10, 0, 8));
    }

    /// <summary>有规则内容 — 高度 = 骨架 12 + 描述 1 + 原因 2（渲染区不得溢出或留白）。</summary>
    [Fact]
    public void MeasureHeight_WithRuleContent_AddsTwoRows()
    {
        var item = new PermissionRequestItem(BeginCoordinator("WebFetch", "是否允许访问？", "domain:example.com"));

        Assert.Equal(15, item.MeasureHeight(width: 60));
    }

    /// <summary>原因区与 Bash 形态共存 — 命令区之后才是原因区（形态数据优先，原因是补充说明）。</summary>
    [Fact]
    public void Render_BashFormWithRuleContent_ReasonAfterCommandRegion()
    {
        var coordinator = BeginCoordinator("Bash", "需要确认: rm -rf /tmp/x", "bash(rm:*)");
        using var screen = new Screen(cols: 60, rows: 20);
        new PermissionRequestItem(coordinator).Render(screen, new Rect(0, 0, 60, 20), Theme.Dark);

        // row5 描述 / row6 命令 / row7 危险警告 → row8、row9 原因区
        Assert.Equal("   rm -rf /tmp/x", RowText.Of(screen, 6, 0, 16));
        Assert.Equal("   触发原因: Bash(bash(rm:*))", RowText.Of(screen, 8, 0, 60).TrimEnd());
        Assert.Equal("   /permissions 更新规则", RowText.Of(screen, 9, 0, 60).TrimEnd());
    }

    // ---- 无规则内容 ----

    /// <summary>无规则内容 — 整区折叠，高度与 P5 之前完全一致（零行为变化锚点）。</summary>
    [Fact]
    public void MeasureHeight_WithoutRuleContent_Unchanged()
    {
        var item = new PermissionRequestItem(BeginCoordinator("WebFetch", "是否允许访问？", string.Empty));

        Assert.Equal(13, item.MeasureHeight(width: 60));
    }

    /// <summary>无规则内容 — 不渲染任何原因字样，问题行回到骨架位置 row7。</summary>
    [Fact]
    public void Render_WithoutRuleContent_HasNoReasonRegion()
    {
        var coordinator = BeginCoordinator("WebFetch", "是否允许访问？", string.Empty);
        using var screen = new Screen(cols: 60, rows: 15);
        new PermissionRequestItem(coordinator).Render(screen, new Rect(0, 0, 60, 15), Theme.Dark);

        Assert.Equal(" Do you want to proceed?", RowText.Of(screen, 7, 0, 24));
        for (var row = 0; row < 15; row++)
        {
            Assert.DoesNotContain("触发原因", RowText.Of(screen, row, 0, 60), StringComparison.Ordinal);
            Assert.DoesNotContain("/permissions", RowText.Of(screen, row, 0, 60), StringComparison.Ordinal);
        }
    }
}
