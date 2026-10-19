namespace NativeTui.Core.Components;

/// <summary>滚动区渲染项 — 滚动内容的最小单元（消息/分隔等）。</summary>
public interface IScrollItem
{
    /// <summary>按可用宽度测量的内容高度。</summary>
    int MeasureHeight(int width);

    /// <summary>渲染到自身完整帧区域（Y 已含滚动平移，可能为负/越界；越界写入由 Screen 忽略实现裁剪）。</summary>
    void Render(Screen screen, Rect frame, Theme theme);
}

/// <summary>
/// 滚动区 — 对齐基准 ink/components/ScrollBox.tsx 的视口语义：
/// 内容按全高自上而下布局，仅渲染与 [scrollTop, scrollTop+height) 相交的项并平移 -scrollTop；
/// sticky 钉底（内容增长时跟随底部，基准 stickyScroll 属性，首渲染即生效）；
/// ScrollBy 打破 sticky（基准 scrollBy：纯累加器，边界在渲染时钳制）；
/// 位置跟随：非 sticky 但 scrollTop 恰在旧 max 且内容增长 → 重新钉底（基准 positional follow）。
/// </summary>
public sealed class ScrollBox
{
    private readonly List<IScrollItem> _items = [];
    private int _lastMaxScroll = -1;
    private int _lastScrollTop;

    /// <summary>创建滚动区（stickyScroll 指定首渲染是否钉底）。</summary>
    public ScrollBox(bool stickyScroll = false)
    {
        Sticky = stickyScroll;
    }

    /// <summary>是否钉底（stickyScroll，含位置跟随触发）。</summary>
    public bool Sticky { get; private set; }

    /// <summary>当前滚动位置（上次渲染结算后的值）。</summary>
    public int ScrollTop { get; private set; }

    /// <summary>追加内容项（钉底/跟随在下次 Render 时结算）。</summary>
    public void Add(IScrollItem item) => _items.Add(item);

    /// <summary>内容项只读视图（驱动层与测试断言用；变更走 Add/Insert）。</summary>
    public IReadOnlyList<IScrollItem> Items => _items;

    /// <summary>内容项数量。</summary>
    public int Count => _items.Count;

    /// <summary>内容总高度（全部项按指定宽度测量求和）— inline 模式建屏（屏高 = 内容高）用。</summary>
    public int MeasureContentHeight(int width) => _items.Sum(item => item.MeasureHeight(width));

    /// <summary>
    /// 在指定位置插入内容项 — REPL 尾部常驻项（spinner）场景：
    /// 新消息必须插到常驻项之前（Insert(Count - 1, msg)），保证常驻项始终最后。
    /// </summary>
    public void Insert(int index, IScrollItem item) => _items.Insert(index, item);

    /// <summary>相对滚动（打破 sticky；边界钳制延迟到渲染，对齐基准 pendingScrollDelta 累加语义）。</summary>
    public void ScrollBy(int dy)
    {
        Sticky = false;
        ScrollTop = Math.Max(0, ScrollTop + dy);
    }

    /// <summary>钉底（基准 scrollToBottom：sticky=true）。</summary>
    public void ScrollToBottom() => Sticky = true;

    /// <summary>
    /// 渲染：测量全部项 → 结算 sticky/位置跟随 → 钳制 scrollTop 到 [0, maxScroll]
    /// → 逐项以完整自身 frame（平移 -scrollTop，可能为负/越界）绘制，
    /// 越界写入由 Screen 索引器忽略实现裁剪；完全在视口外的项跳过（viewport culling）。
    /// </summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        var heights = _items.Select(item => item.MeasureHeight(frame.Width)).ToArray();
        var contentHeight = heights.Sum();
        var maxScroll = Math.Max(0, contentHeight - frame.Height);
        if (Sticky || (_lastMaxScroll >= 0 && _lastScrollTop == _lastMaxScroll && maxScroll > _lastMaxScroll))
        {
            Sticky = true;
            ScrollTop = maxScroll;
        }
        else
        {
            ScrollTop = Math.Clamp(ScrollTop, 0, maxScroll);
        }

        var windowBottom = ScrollTop + frame.Height;
        var itemTop = 0;
        for (var i = 0; i < _items.Count; i++)
        {
            var itemBottom = itemTop + heights[i];
            if (itemBottom > ScrollTop && itemTop < windowBottom)
            {
                var itemFrame = new Rect(frame.X, frame.Y + itemTop - ScrollTop, frame.Width, heights[i]);
                _items[i].Render(screen, itemFrame, theme);
            }

            itemTop = itemBottom;
        }

        _lastMaxScroll = maxScroll;
        _lastScrollTop = ScrollTop;
    }
}
