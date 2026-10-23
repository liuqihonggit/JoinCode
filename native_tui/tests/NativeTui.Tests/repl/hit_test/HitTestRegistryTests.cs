namespace NativeTui.Tests.Repl.HitTest;

/// <summary>
/// HitTestRegistry 语义测试 — 对齐基准 ink/hit-test.ts:18-41：
/// 半开区间边界（:26-29 越界判定，右/下不含）、后绘制兄弟优先（:33-39 childNodes 反向遍历）、
/// 帧级 Clear 重建（:8-10 nodeCache 每帧重填 — 未注册即 null）；
/// 另验 IScrollItem.HitTestId 默认接口实现（DIM）零破坏 — 未显式实现的假 item 经接口引用读出 null。
/// </summary>
public sealed class HitTestRegistryTests
{
    /// <summary>不声明 HitTestId 的最小假 item — DIM 契约验证（现有实现类零破坏的缩影）。</summary>
    private sealed record BlankItem : IScrollItem
    {
        /// <summary>单行高度。</summary>
        public int MeasureHeight(int width) => 1;

        /// <summary>空渲染 — 命中测试不消费 cell 内容。</summary>
        public void Render(Screen screen, Rect frame, Theme theme)
        {
        }
    }

    /// <summary>注册后 rect 内各边界命中 — 左上角（含）/ 右下角最后 cell（半开区间下含 (row+h-1, col+w-1)）/ 中间。</summary>
    [Fact]
    public void RegisterThenHit_InsideRectAllCorners()
    {
        var registry = new HitTestRegistry();
        registry.Register("item-a", row: 2, col: 3, width: 5, height: 4);

        Assert.Equal("item-a", registry.HitTest(row: 2, col: 3));
        Assert.Equal("item-a", registry.HitTest(row: 5, col: 7));
        Assert.Equal("item-a", registry.HitTest(row: 3, col: 5));
    }

    /// <summary>rect 外不命中 — 四方向越界 + 半开区间精确边界（col+width / row+height 恰出界不含，基准 :26-29）。</summary>
    [Fact]
    public void OutsideRect_NoHit()
    {
        var registry = new HitTestRegistry();
        registry.Register("item-a", row: 2, col: 3, width: 5, height: 4);

        Assert.Null(registry.HitTest(row: 1, col: 3));
        Assert.Null(registry.HitTest(row: 6, col: 3));
        Assert.Null(registry.HitTest(row: 2, col: 2));
        Assert.Null(registry.HitTest(row: 2, col: 8));
        Assert.Null(registry.HitTest(row: 6, col: 8));
    }

    /// <summary>重叠区域后注册优先 — 两 item rect 重叠，重叠点命中的是后注册 id（后绘制兄弟顶层优先，基准 :33-39）；
    /// 非重叠区仍归先注册 item。</summary>
    [Fact]
    public void OverlappingRects_LaterRegisterWins()
    {
        var registry = new HitTestRegistry();
        registry.Register("under", row: 0, col: 0, width: 10, height: 10);
        registry.Register("over", row: 5, col: 5, width: 5, height: 5);

        Assert.Equal("over", registry.HitTest(row: 7, col: 7));
        Assert.Equal("under", registry.HitTest(row: 1, col: 1));
    }

    /// <summary>Clear 后不命中 — 帧级重建语义（nodeCache 每帧重填，基准 :8-10）：清空后原 rect 区域返回 null。</summary>
    [Fact]
    public void AfterClear_NoHit()
    {
        var registry = new HitTestRegistry();
        registry.Register("item-a", row: 0, col: 0, width: 10, height: 10);
        Assert.NotNull(registry.HitTest(row: 5, col: 5));

        registry.Clear();

        Assert.Null(registry.HitTest(row: 5, col: 5));
        Assert.Equal(0, registry.Count);
    }

    /// <summary>未注册区域 null — 空表查询直接返回 null（基准 nodeCache 无 rect 节点跳过语义，:24）。</summary>
    [Fact]
    public void UnregisteredArea_ReturnsNull()
    {
        var registry = new HitTestRegistry();

        Assert.Null(registry.HitTest(row: 0, col: 0));
        Assert.Null(registry.HitTest(row: 99, col: 99));
        Assert.Equal(0, registry.Count);
    }

    /// <summary>同 id 多 rect 各自命中 — item 分块声明（多行 item 标题/参数分离注册），两块区域内同 id。</summary>
    [Fact]
    public void SameIdMultipleRects_EachHits()
    {
        var registry = new HitTestRegistry();
        registry.Register("item-a", row: 1, col: 0, width: 20, height: 1);
        registry.Register("item-a", row: 3, col: 0, width: 20, height: 2);

        Assert.Equal("item-a", registry.HitTest(row: 1, col: 19));
        Assert.Equal("item-a", registry.HitTest(row: 4, col: 0));
        Assert.Null(registry.HitTest(row: 2, col: 0));
        Assert.Equal(2, registry.Count);
    }

    /// <summary>IScrollItem 默认 HitTestId 为 null — DIM 零破坏契约：未显式实现的 item 经接口引用读默认 null
    /// （HitTestId 为 null = 不参与命中，由接线方跳过注册）。</summary>
    [Fact]
    public void ScrollItemDefault_HitTestIdIsNull()
    {
        IScrollItem item = new BlankItem();

        Assert.Null(item.HitTestId);
    }
}
