namespace NativeTui.Tests.Layout;

/// <summary>
/// flexbox 子集布局引擎测试 — REPL 布局所需能力：
/// column/row 堆叠、flexGrow 剩余空间分配、padding、gap、显式尺寸、交叉轴 stretch。
/// 对齐基准 ink/layout/engine.ts 的 LayoutNode 语义（Yoga 的最小子集）。
/// </summary>
public sealed class LayoutEngineTests
{
    [Fact]
    public void Column_StacksChildrenVertically()
    {
        var root = LayoutNode.Column(
            LayoutNode.Fixed(height: 1),
            LayoutNode.Fixed(height: 2));
        root.Measure(width: 10, height: 10);
        Assert.Equal(new Rect(0, 0, 10, 1), root.Children[0].Frame);
        Assert.Equal(new Rect(0, 1, 10, 2), root.Children[1].Frame);
    }

    [Fact]
    public void Row_StacksChildrenHorizontally()
    {
        var root = LayoutNode.Row(
            LayoutNode.Fixed(width: 3),
            LayoutNode.Fixed(width: 5));
        root.Measure(width: 10, height: 2);
        Assert.Equal(new Rect(0, 0, 3, 2), root.Children[0].Frame);
        Assert.Equal(new Rect(3, 0, 5, 2), root.Children[1].Frame);
    }

    [Fact]
    public void FlexGrow_FillsRemainingSpace()
    {
        var root = LayoutNode.Column(
            LayoutNode.Expand(),
            LayoutNode.Fixed(height: 3));
        root.Measure(width: 10, height: 10);
        Assert.Equal(new Rect(0, 0, 10, 7), root.Children[0].Frame);
        Assert.Equal(new Rect(0, 7, 10, 3), root.Children[1].Frame);
    }

    [Fact]
    public void MultipleGrow_SplitByRatio()
    {
        var root = LayoutNode.Column(
            LayoutNode.Expand(grow: 1),
            LayoutNode.Expand(grow: 3));
        root.Measure(width: 10, height: 8);
        // 1:3 → 2 + 6
        Assert.Equal(2, root.Children[0].Frame.Height);
        Assert.Equal(6, root.Children[1].Frame.Height);
    }

    [Fact]
    public void Padding_OffsetsChildren()
    {
        var root = LayoutNode.Column(LayoutNode.Fixed(height: 1)) with { Padding = 2 };
        root.Measure(width: 10, height: 10);
        Assert.Equal(new Rect(2, 2, 6, 1), root.Children[0].Frame);
    }

    [Fact]
    public void Gap_SpacesChildren()
    {
        var root = LayoutNode.Column(
            LayoutNode.Fixed(height: 1),
            LayoutNode.Fixed(height: 1)) with { Gap = 1 };
        root.Measure(width: 10, height: 10);
        Assert.Equal(0, root.Children[0].Frame.Y);
        Assert.Equal(2, root.Children[1].Frame.Y);
    }

    [Fact]
    public void CrossAxis_StretchByDefault()
    {
        var root = LayoutNode.Column(
            LayoutNode.Fixed(height: 1));
        root.Measure(width: 12, height: 10);
        Assert.Equal(12, root.Children[0].Frame.Width);
    }

    [Fact]
    public void ExplicitWidth_ChildRespectedInRow()
    {
        var root = LayoutNode.Row(
            LayoutNode.Fixed(width: 4, height: 1));
        root.Measure(width: 12, height: 10);
        Assert.Equal(4, root.Children[0].Frame.Width);
        Assert.Equal(1, root.Children[0].Frame.Height);
    }

    [Fact]
    public void AutoHeight_InColumn_WrapsContent()
    {
        // 无显式高度且非 grow 的子项 = auto：按子内容测量
        var inner = LayoutNode.Column(
            LayoutNode.Fixed(height: 2));
        var root = LayoutNode.Column(inner);
        root.Measure(width: 10, height: 20);
        Assert.Equal(2, inner.Frame.Height);
    }

    [Fact]
    public void NestedColumn_PropagatesConstraints()
    {
        var messages = LayoutNode.Expand();
        var input = LayoutNode.Fixed(height: 3);
        var footer = LayoutNode.Fixed(height: 1);
        var root = LayoutNode.Column(messages, input, footer);
        root.Measure(width: 80, height: 24);
        // REPL 骨架：消息区 20 行 + 输入 3 行 + footer 1 行
        Assert.Equal(new Rect(0, 0, 80, 20), messages.Frame);
        Assert.Equal(new Rect(0, 20, 80, 3), input.Frame);
        Assert.Equal(new Rect(0, 23, 80, 1), footer.Frame);
    }

    [Fact]
    public void ChildLargerThanParent_Clamped()
    {
        var root = LayoutNode.Column(
            LayoutNode.Fixed(height: 30));
        root.Measure(width: 10, height: 10);
        Assert.Equal(10, root.Children[0].Frame.Height);
    }

    [Fact]
    public void RowInsideColumn_HorizontalThenVertical()
    {
        var row = new LayoutNode { Direction = LayoutDirection.Row, FlexGrow = 1 };
        row.Children.Add(LayoutNode.Fixed(width: 2));
        row.Children.Add(LayoutNode.Fixed(width: 3));
        var root = LayoutNode.Column(row, LayoutNode.Fixed(height: 1));
        root.Measure(width: 10, height: 5);
        // row Expand → 高 4；row 内子项交叉轴 stretch 到 4
        Assert.Equal(new Rect(0, 0, 2, 4), row.Children[0].Frame);
        Assert.Equal(new Rect(2, 0, 3, 4), row.Children[1].Frame);
        Assert.Equal(new Rect(0, 4, 10, 1), root.Children[1].Frame);
    }

    [Fact]
    public void FlexShrink_ShrinksEvenlyWhenOverflow()
    {
        // 总需求 12 > 容器 8，缺 4 均匀分摊 → a=2, b=6（TUI 常态 shrink 全 1 时与 Yoga 等价的整数简化）
        var a = LayoutNode.Fixed(width: 4) with { FlexShrink = 1 };
        var b = LayoutNode.Fixed(width: 8) with { FlexShrink = 1 };
        var root = LayoutNode.Row(a, b);
        root.Measure(width: 8, height: 2);
        Assert.Equal(2, a.Frame.Width);
        Assert.Equal(6, b.Frame.Width);
    }
}
