namespace NativeTui.Core.Components;

/// <summary>滚动区渲染项 — 滚动内容的最小单元（消息/分隔等）。</summary>
public interface IScrollItem
{
    /// <summary>按可用宽度测量的内容高度。</summary>
    int MeasureHeight(int width);

    /// <summary>渲染到自身完整帧区域（Y 已含滚动平移，可能为负/越界；越界写入由 Screen 忽略实现裁剪）。</summary>
    void Render(Screen screen, Rect frame, Theme theme);

    /// <summary>
    /// 命中测试标识（TASK066 板块 C）— null = 不参与命中（默认）。声明非空 id 的 item
    /// 由渲染接线（ReplView 帧序）向 HitTestRegistry 注册自身 rect（HitTest 同 id 反向遍历）。
    /// id 建议格式「域:键」（如 tool-call:3 / background-task:fork:42 — 命名建议见 TASK066 交付清单）。
    /// 默认接口实现（DIM）零破坏 — 现有实现类无需改动即编译通过；注意：默认实现仅经
    /// IScrollItem 接口引用可见（ScrollBox.Items 遍历语境天然满足 — 具体类型引用需显式实现）。
    /// </summary>
    string? HitTestId => null;
}

/// <summary>
/// 滚动区 — 对齐基准 ink/components/ScrollBox.tsx 的视口语义：
/// 内容按全高自上而下布局，仅渲染与 [scrollTop, scrollTop+height) 相交的项并平移 -scrollTop；
/// sticky 钉底（内容增长时跟随底部，基准 stickyScroll 属性，首渲染即生效）；
/// ScrollBy 打破 sticky（基准 scrollBy：纯累加器，边界在渲染时钳制）；
/// 位置跟随：非 sticky 但 scrollTop 恰在旧 max 且内容增长 → 重新钉底（基准 positional follow）。
/// 虚拟滚动增强（TASK066 F6 — 基准 useVirtualScroll.ts）：<see cref="OverscanRows"/> &gt; 0 时
/// 渲染窗口扩为视口上下各 N 行（基准 OVERSCAN_ROWS=80 — 真实高度可达估值 10 倍故从宽）：
/// 窗口内项保持 Render 调用（动画/状态副作用 + 滚动方向预渲染，平滑滚动不闪帧），
/// 写入经 <see cref="Screen.ClipScope(Layout.Rect)"/> 限制在 frame 内（对齐基准 ink 输出层视口裁剪）；
/// 窗口外更远项跳过 Render（离屏冻结 — 对齐基准 OffscreenFreeze 冻结=渲染跳过：
/// MeasureHeight 仍全量计高占位，滚动位置语义不变）。默认 0 = 精确视口 culling 既有语义零破坏。
/// </summary>
public sealed class ScrollBox
{
    /// <summary>默认 overscan 行数（TASK066 F6 — 基准 useVirtualScroll.ts:24 OVERSCAN_ROWS=80：
    /// 视口上下各预留渲染行数；"Generous because real heights can be 10x the estimate"）。</summary>
    public const int DefaultOverscanRows = 80;

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

    /// <summary>视口外上下各预留渲染行数（TASK066 F6 — 0 = 精确视口 culling 既有语义，默认零破坏；
    /// &gt; 0 = 渲染窗口 [scrollTop-overscan, windowBottom+overscan) — 窗口内项 Render 照常调用
    ///（写入按帧 clip）、窗口外项跳过渲染高度保留。基准 useVirtualScroll.ts:24 = 80。</summary>
    public int OverscanRows { get; set; }

    /// <summary>上次 Render 实际调用渲染的项数（TASK066 F6 — overscan 窗口/离屏冻结测试断言面：
    /// 精确视口 = 相交项数；overscan 窗口 = 视口±N 行内项数（更远项冻结不渲染））。</summary>
    internal int LastRenderedCount { get; private set; }

    /// <summary>选中高亮项（消息光标导航 — 交互层 Step1）— 渲染后整块背景覆写
    /// MessageActionsBackground（前景字符保留）；null = 无高亮。基准消息选中背景语义。</summary>
    public IScrollItem? HighlightItem { get; set; }

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

    /// <summary>移除指定位置内容项（权限对话框完成/收尾移除场景 — 项类型由调用方判定）。</summary>
    public void RemoveAt(int index) => _items.RemoveAt(index);

    /// <summary>查找内容项索引（流式项替换/清理场景 — Driver 定位原位置；未找到 -1）。</summary>
    public int IndexOf(IScrollItem item) => _items.IndexOf(item);

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
    /// 完全在渲染窗口外的项跳过（viewport culling + overscan，TASK066 F6）：
    /// 窗口 = [scrollTop - OverscanRows, windowBottom + OverscanRows)（0 = 精确视口既有语义）；
    /// 渲染期 clip 到自身 frame（<see cref="Screen.ClipScope(Layout.Rect)"/> — 项越界写入丢弃，
    /// Render 副作用（动画推进/状态更新）保留，对齐基准 ink 输出层视口裁剪）。
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

        var overscan = OverscanRows;
        var windowTop = ScrollTop - overscan;
        var windowBottom = ScrollTop + frame.Height + overscan;
        var rendered = 0;
        var itemTop = 0;
        using (screen.ClipScope(frame))
        {
            for (var i = 0; i < _items.Count; i++)
            {
                var itemBottom = itemTop + heights[i];
                if (itemBottom > windowTop && itemTop < windowBottom)
                {
                    var itemFrame = new Rect(frame.X, frame.Y + itemTop - ScrollTop, frame.Width, heights[i]);
                    _items[i].Render(screen, itemFrame, theme);
                    rendered++;
                    if (ReferenceEquals(_items[i], HighlightItem))
                    {
                        ApplyHighlightBackground(screen, itemFrame, theme);
                    }
                }

                itemTop = itemBottom;
            }
        }

        LastRenderedCount = rendered;
        _lastMaxScroll = maxScroll;
        _lastScrollTop = ScrollTop;
    }

    /// <summary>整块背景覆写 — 选中项 frame 区域全行换 MessageActionsBackground（字符与前景保留，
    /// 越界写入由 Screen 索引器忽略裁剪；含 margin 行 — 基准 React 包裹整条消息语义）。</summary>
    private static void ApplyHighlightBackground(Screen screen, Rect frame, Theme theme)
    {
        for (var row = frame.Y; row < frame.Y + frame.Height; row++)
        {
            for (var col = frame.X; col < frame.X + frame.Width; col++)
            {
                var cell = screen[row, col];
                screen[row, col] = cell with
                {
                    StyleId = screen.Styles.GetOrAdd(screen.Styles.Get(cell.StyleId) with { Background = theme.MessageActionsBackground }),
                };
            }
        }
    }
}
