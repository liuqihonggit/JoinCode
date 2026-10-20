namespace NativeTui.Core.Layout;

/// <summary>布局主轴方向。</summary>
public enum LayoutDirection : byte
{
    /// <summary>水平排列。</summary>
    Row = 0,

    /// <summary>垂直排列（REPL 主布局）。</summary>
    Column = 1,
}

/// <summary>矩形（cell 坐标，1 格 = 1 单位）。</summary>
public readonly record struct Rect(int X, int Y, int Width, int Height)
{
    /// <summary>右边界列（不含）。</summary>
    public int Right => X + Width;

    /// <summary>下边界行（不含）。</summary>
    public int Bottom => Y + Height;
}

/// <summary>
/// flexbox 子集布局节点 — 对齐基准 ink/layout/engine.ts（Yoga 最小子集）。
/// 支持：Row/Column、flexGrow（比例分配剩余）、flexShrink（均匀收缩）、
/// 显式 width/height、四边 padding、gap、交叉轴 stretch（TUI 常态）。
/// auto 高度按内容测量（MeasureContent 钩子）。
/// </summary>
public sealed record LayoutNode
{
    /// <summary>子节点。</summary>
    public List<LayoutNode> Children { get; } = [];

    /// <summary>主轴方向。</summary>
    public LayoutDirection Direction { get; init; }

    /// <summary>显式宽（null = auto/stretch）。</summary>
    public int? Width { get; init; }

    /// <summary>显式高（null = auto/stretch）。</summary>
    public int? Height { get; init; }

    /// <summary>剩余空间分配权重。</summary>
    public double FlexGrow { get; init; }

    /// <summary>溢出收缩权重（0 = 不收缩）。</summary>
    public double FlexShrink { get; init; }

    /// <summary>四边内边距。</summary>
    public int Padding { get; init; }

    /// <summary>子项间距。</summary>
    public int Gap { get; init; }

    /// <summary>内容测量钩子（叶子组件提供 auto 尺寸来源）。</summary>
    public Func<int>? MeasureContentWidth { get; init; }

    /// <summary>内容测量钩子。</summary>
    public Func<int>? MeasureContentHeight { get; init; }

    /// <summary>计算结果帧（相对父容器内容区）。</summary>
    public Rect Frame { get; internal set; }

    /// <summary>创建空节点（属性用 init/with 配置）。</summary>
    public LayoutNode()
    {
    }

    /// <summary>列容器。</summary>
    public static LayoutNode Column(params LayoutNode[] children) => New(LayoutDirection.Column, children);

    /// <summary>行容器。</summary>
    public static LayoutNode Row(params LayoutNode[] children) => New(LayoutDirection.Row, children);

    /// <summary>显式尺寸节点。</summary>
    public static LayoutNode Fixed(int? width = null, int? height = null) =>
        new() { Width = width, Height = height };

    /// <summary>flexGrow 节点（撑满剩余空间）。</summary>
    public static LayoutNode Expand(double grow = 1) => new() { FlexGrow = grow };

    private static LayoutNode New(LayoutDirection direction, LayoutNode[] children)
    {
        var node = new LayoutNode { Direction = direction };
        node.Children.AddRange(children);
        return node;
    }

    /// <summary>以给定约束测量整棵树（写入各节点 Frame）。</summary>
    public void Measure(int width, int height)
    {
        Frame = new Rect(0, 0, width, height);
        MeasureChildren(width, height);
    }

    private void MeasureChildren(int containerWidth, int containerHeight)
    {
        var innerWidth = Math.Max(0, containerWidth - 2 * Padding);
        var innerHeight = Math.Max(0, containerHeight - 2 * Padding);
        var isColumn = Direction == LayoutDirection.Column;
        var mainSize = isColumn ? innerHeight : innerWidth;
        var crossSize = isColumn ? innerWidth : innerHeight;
        var totalGap = Children.Count > 1 ? Gap * (Children.Count - 1) : 0;

        // 第一遍：确定每个子项的主轴基准尺寸
        var basis = new int[Children.Count];
        var totalBasis = 0;
        var totalGrow = 0.0;
        var shrinkCount = 0;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            var childBasis = isColumn
                ? child.Height ?? MeasureAutoHeight(child)
                : child.Width ?? MeasureAutoWidth(child);
            basis[i] = childBasis;
            totalBasis += childBasis;
            totalGrow += child.FlexGrow;
            if (child.FlexShrink > 0)
            {
                shrinkCount++;
            }
        }

        // 第二遍：分配主轴尺寸（grow 撑剩余 / shrink 收溢出）
        var main = new int[Children.Count];
        var available = mainSize - totalGap;
        if (totalBasis <= available)
        {
            var free = available - totalBasis;
            var growAllocated = 0;
            for (var i = 0; i < Children.Count; i++)
            {
                main[i] = basis[i];
                if (totalGrow > 0 && Children[i].FlexGrow > 0)
                {
                    var share = (int)Math.Floor(free * Children[i].FlexGrow / totalGrow);
                    main[i] += share;
                    growAllocated += share;
                }
            }

            DistributeRemainder(free - growAllocated, main);
        }
        else if (shrinkCount > 0)
        {
            var overflow = totalBasis - available;
            var shrinkEach = overflow / shrinkCount;
            var remainder = overflow % shrinkCount;
            var shrunk = 0;
            for (var i = 0; i < Children.Count; i++)
            {
                main[i] = basis[i];
                if (Children[i].FlexShrink > 0)
                {
                    var cut = shrinkEach + (shrunk < remainder ? 1 : 0);
                    main[i] = Math.Max(0, main[i] - cut);
                    shrunk++;
                }
            }
        }
        else
        {
            // 无收缩：clamp 到可用空间
            var offset = 0;
            for (var i = 0; i < Children.Count; i++)
            {
                main[i] = Math.Min(basis[i], Math.Max(0, available - offset));
                offset += main[i];
            }
        }

        // 第三遍：定位 + 交叉轴 stretch + 递归
        var position = Padding;
        for (var i = 0; i < Children.Count; i++)
        {
            var child = Children[i];
            var childCross = isColumn
                ? child.Width ?? crossSize
                : child.Height ?? crossSize;
            childCross = Math.Min(childCross, crossSize);
            var frame = isColumn
                ? new Rect(Padding, position, childCross, main[i])
                : new Rect(position, Padding, main[i], childCross);
            child.Frame = frame;
            child.MeasureChildren(frame.Width, frame.Height);
            position += main[i] + Gap;
        }
    }

    private void DistributeRemainder(int remainder, int[] main)
    {
        if (remainder <= 0)
        {
            return;
        }

        // grow 取整后的余量分给靠前的 grow 项（确定性，不依赖浮点尾差）
        for (var i = 0; i < Children.Count && remainder > 0; i++)
        {
            if (Children[i].FlexGrow > 0)
            {
                main[i]++;
                remainder--;
            }
        }
    }

    private static int MeasureAutoHeight(LayoutNode node)
    {
        if (node.Height is { } explicitHeight)
        {
            return explicitHeight;
        }

        if (node.MeasureContentHeight is { } measure)
        {
            return measure();
        }

        if (node.Children.Count == 0)
        {
            return 0;
        }

        var totalGap = node.Children.Count > 1 ? node.Gap * (node.Children.Count - 1) : 0;
        if (node.Direction == LayoutDirection.Column)
        {
            var sum = 0;
            foreach (var child in node.Children)
            {
                sum += MeasureAutoHeight(child);
            }

            return sum + totalGap + 2 * node.Padding;
        }

        var max = 0;
        foreach (var child in node.Children)
        {
            max = Math.Max(max, MeasureAutoHeight(child));
        }

        return max + 2 * node.Padding;
    }

    private static int MeasureAutoWidth(LayoutNode node)
    {
        if (node.Width is { } explicitWidth)
        {
            return explicitWidth;
        }

        if (node.MeasureContentWidth is { } measure)
        {
            return measure();
        }

        if (node.Children.Count == 0)
        {
            return 0;
        }

        var totalGap = node.Children.Count > 1 ? node.Gap * (node.Children.Count - 1) : 0;
        if (node.Direction == LayoutDirection.Row)
        {
            var sum = 0;
            foreach (var child in node.Children)
            {
                sum += MeasureAutoWidth(child);
            }

            return sum + totalGap + 2 * node.Padding;
        }

        var max = 0;
        foreach (var child in node.Children)
        {
            max = Math.Max(max, MeasureAutoWidth(child));
        }

        return max + 2 * node.Padding;
    }
}
