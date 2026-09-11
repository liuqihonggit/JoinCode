namespace Hands.Tests.ToolHandlers;

/// <summary>
/// QuadtreeSplitAnimator 单元测试 — 验证四叉树分裂计算(框坐标/颜色淡化/线宽)
/// </summary>
public sealed class QuadtreeSplitAnimatorTests
{
    private const int ScreenW = 1920;
    private const int ScreenH = 1080;

    [Fact]
    public void GetLayerRects_Depth0_ReturnsSingleFullScreenRect()
    {
        var rects = QuadtreeSplitAnimator.GetLayerRects(0, 0, ScreenW, ScreenH, 0);

        rects.Should().HaveCount(1);
        rects[0].Should().Be(new QuadtreeRect(0, 0, ScreenW, ScreenH));
    }

    [Fact]
    public void GetLayerRects_Depth1_Returns4Quadrants()
    {
        var rects = QuadtreeSplitAnimator.GetLayerRects(0, 0, ScreenW, ScreenH, 1);

        rects.Should().HaveCount(4);
        var halfW = ScreenW / 2;
        var halfH = ScreenH / 2;

        rects[0].Should().Be(new QuadtreeRect(0, 0, halfW, halfH));
        rects[1].Should().Be(new QuadtreeRect(halfW, 0, ScreenW - halfW, halfH));
        rects[2].Should().Be(new QuadtreeRect(0, halfH, halfW, ScreenH - halfH));
        rects[3].Should().Be(new QuadtreeRect(halfW, halfH, ScreenW - halfW, ScreenH - halfH));
    }

    [Fact]
    public void GetLayerRects_Depth2_Returns16Rects()
    {
        var rects = QuadtreeSplitAnimator.GetLayerRects(0, 0, ScreenW, ScreenH, 2);
        rects.Should().HaveCount(16);
    }

    [Fact]
    public void GetLayerRects_Depth3_Returns64Rects()
    {
        var rects = QuadtreeSplitAnimator.GetLayerRects(0, 0, ScreenW, ScreenH, 3);
        rects.Should().HaveCount(64);
    }

    [Fact]
    public void GetLayerRects_ChildRectsCoverParentExactly()
    {
        var parents = QuadtreeSplitAnimator.GetLayerRects(0, 0, ScreenW, ScreenH, 1);
        var children = QuadtreeSplitAnimator.GetLayerRects(0, 0, ScreenW, ScreenH, 2);

        foreach (var p in parents)
        {
            var pChildren = children.Where(c => c.X >= p.X && c.X + c.Width <= p.X + p.Width &&
                                                c.Y >= p.Y && c.Y + c.Height <= p.Y + p.Height).ToList();
            pChildren.Should().HaveCount(4, "每个父框应恰好分裂为4个子框");

            var totalArea = pChildren.Sum(c => (long)c.Width * c.Height);
            var parentArea = (long)p.Width * p.Height;
            totalArea.Should().Be(parentArea, "子框总面积应等于父框面积(无重叠无遗漏)");
        }
    }

    [Fact]
    public void GetLayerRects_OddScreenSize_HandlesRoundingCorrectly()
    {
        var rects = QuadtreeSplitAnimator.GetLayerRects(0, 0, 1001, 501, 1);

        rects.Should().HaveCount(4);
        var halfW = 1001 / 2;
        var halfH = 501 / 2;

        rects[1].Width.Should().Be(1001 - halfW, "奇数宽度时右半部分应补齐");
        rects[3].Height.Should().Be(501 - halfH, "奇数高度时下半部分应补齐");
    }

    [Fact]
    public void FadeColor_Depth0_ReturnsBaseColor()
    {
        const uint blue = 0x00FF0000;
        var result = QuadtreeSplitAnimator.FadeColor(blue, 0, 3);
        result.Should().Be(blue);
    }

    [Fact]
    public void FadeColor_MaxDepth_ReturnsNearWhite()
    {
        const uint blue = 0x00FF0000;
        var result = QuadtreeSplitAnimator.FadeColor(blue, 3, 3);
        var r = result & 0xFF;
        var g = (result >> 8) & 0xFF;
        var b = (result >> 16) & 0xFF;
        r.Should().Be(255);
        g.Should().Be(255);
        b.Should().Be(255);
    }

    [Fact]
    public void FadeColor_IntermediateDepth_ReturnsInterpolated()
    {
        const uint red = 0x000000FF;
        var result = QuadtreeSplitAnimator.FadeColor(red, 1, 2);
        var r = result & 0xFF;
        var g = (result >> 8) & 0xFF;
        var b = (result >> 16) & 0xFF;
        r.Should().Be(255);
        g.Should().BeInRange(120, 130, "G 通道应约为 255*0.5=127");
        b.Should().BeInRange(120, 130, "B 通道应约为 255*0.5=127");
    }

    [Fact]
    public void FadeColor_MaxDepthZero_ReturnsBaseColor()
    {
        const uint color = 0x0000FFFF;
        var result = QuadtreeSplitAnimator.FadeColor(color, 0, 0);
        result.Should().Be(color);
    }

    [Fact]
    public void GetPenWidth_Depth0_Returns5()
    {
        QuadtreeSplitAnimator.GetPenWidth(0).Should().Be(5);
    }

    [Fact]
    public void GetPenWidth_Depth4_Returns1()
    {
        QuadtreeSplitAnimator.GetPenWidth(4).Should().Be(1);
    }

    [Fact]
    public void GetPenWidth_DeepDepth_Returns1()
    {
        QuadtreeSplitAnimator.GetPenWidth(10).Should().Be(1, "深度超过5时线宽应钳制为1");
    }

    [Fact]
    public void GetRectColor_Depth0_ReturnsWhite()
    {
        var color = QuadtreeSplitAnimator.GetRectColor(0, 1, 0, 3);
        color.Should().Be(0x00FFFFFF, "第0层(外框)应为白色");
    }

    [Fact]
    public void GetRectColor_DifferentIndices_ReturnDifferentColors()
    {
        var c0 = QuadtreeSplitAnimator.GetRectColor(0, 4, 1, 3);
        var c1 = QuadtreeSplitAnimator.GetRectColor(1, 4, 1, 3);
        var c2 = QuadtreeSplitAnimator.GetRectColor(2, 4, 1, 3);
        var c3 = QuadtreeSplitAnimator.GetRectColor(3, 4, 1, 3);

        var colors = new[] { c0, c1, c2, c3 };
        colors.Distinct().Should().HaveCount(4, "4个格子应有4种不同颜色");
    }

    [Fact]
    public void GetRectColor_DeeperDepth_IsLighter()
    {
        var shallow = QuadtreeSplitAnimator.GetRectColor(0, 4, 1, 3);
        var deep = QuadtreeSplitAnimator.GetRectColor(0, 64, 3, 3);

        var shallowSum = (shallow & 0xFF) + ((shallow >> 8) & 0xFF) + ((shallow >> 16) & 0xFF);
        var deepSum = (deep & 0xFF) + ((deep >> 8) & 0xFF) + ((deep >> 16) & 0xFF);
        deepSum.Should().BeGreaterThanOrEqualTo(shallowSum - 50, "更深层颜色应更浅(允许容差)");
    }
}
