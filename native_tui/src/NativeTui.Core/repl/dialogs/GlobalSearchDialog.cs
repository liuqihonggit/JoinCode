namespace NativeTui.Core.Repl.Dialogs;

/// <summary>
/// 全局搜索对话框 — 对齐基准 GlobalSearchDialog.tsx（ctrl+shift+f 工作区 rg 搜索）：
/// React useState 族收敛为 C# 显式状态属性 + TryHandleKey 键路由 + MeasureHeight/Render 组件面。
/// 渲染行序（BackgroundTaskDetailDialog 边框框体模式）：margin + permission 顶边框 + 标题
/// "全局搜索"（bold permission）+ 查询行（❯ + 查询回显 + Searching 时行尾 spinner 帧字符 —
/// AnimTimeMs 50ms 帧拍驱动复用 SpinnerMath 12 帧序列，与全局动画时钟同相位）+ 空行 +
/// 结果区（空 = dim 状态行[输入以搜索…/搜索中…/无匹配三分支]；非空 = 可见窗口行[文件:行号
/// dim + 行文本，选中行 ❯ + suggestion 高亮，WriteText rightBound 截断到框宽] + 溢出行）
/// + 空行 + footer（↑↓ 导航 · enter 定位 · esc 关闭，dim）。
/// 键位：字符/退格进查询（SelectedMatch 失效置空）；↑↓ 导航（边界钳制 + 窗口滚动跟随）；
/// enter 置 SelectedMatch（宿主消费后调 Close 接管定位 — 组件不自行关闭）；esc 置 Closed
///（宿主随即摘除组件，关闭后键路由一律不消费）。搜索触发为显式 BeginSearchAsync（防抖留
/// 宿主 — 基准 DEBOUNCE_MS=100 的定时语义由宿主帧拍复刻）；搜索源经 ISearchSource 注入。
/// 重入保护：组件不持有运行中搜索任务 — 防抖重触发时宿主先取消旧查询令牌再调新 BeginSearchAsync
///（取消为常态语义：保留已收集部分结果静默收尾，对齐基准 abortRef 旧搜索中断不报错）。
/// </summary>
public sealed class GlobalSearchDialog : IScrollItem, IAnimatable
{
    /// <summary>结果上限（达到即停收集 + 截断标记 — 基准 MAX_TOTAL_MATCHES 语义的任务书取值 100）。</summary>
    public const int MaxResults = 100;

    /// <summary>默认可见结果行数（基准 VISIBLE_RESULTS=12 的紧凑取值 — 宿主构造可调）。</summary>
    public const int DefaultVisibleResultCount = 8;

    /// <summary>标题（渲染层 1 空格缩进 bold permission 色）。</summary>
    public const string TitleText = "全局搜索";

    /// <summary>查询行前缀（❯ 输入提示符 + 包裹空格）。</summary>
    public const string QueryPrompt = " ❯ ";

    /// <summary>footer 键位说明（MiddotSeparator 分隔 — 对齐基准 Byline 形态）。</summary>
    public const string FooterText =
        $"↑↓ 导航{Figures.MiddotSeparator}enter 定位{Figures.MiddotSeparator}esc 关闭";

    /// <summary>空查询状态文案（基准 "Type to search…" 汉化）。</summary>
    private const string StatusTypeToSearch = "输入以搜索…";

    /// <summary>搜索中状态文案（基准 "Searching…" 汉化）。</summary>
    private const string StatusSearching = "搜索中…";

    /// <summary>无匹配状态文案（基准 "No matches" 汉化）。</summary>
    private const string StatusNoMatches = "无匹配";

    private readonly string _rootPath;

    private readonly ISearchSource? _source;

    /// <summary>结果窗口起点（导航滚动跟随 — 选中项滚出可见区时平移）。</summary>
    private int _windowStart;

    /// <summary>创建对话框 — rootPath 为搜索根（透传搜索源）；searchSource 注入搜索后端
    ///（null = 无后端纯状态机/UI — 纯 UI 场景与测试注入）；visibleResultCount 可见结果行数（≥1）。</summary>
    /// <param name="rootPath">搜索根目录（绝对路径形态由宿主决定）。</param>
    /// <param name="searchSource">搜索源（null = BeginSearchAsync 空转清场）。</param>
    /// <param name="visibleResultCount">可见结果行数（结果窗口高度）。</param>
    public GlobalSearchDialog(string rootPath, ISearchSource? searchSource = null, int visibleResultCount = DefaultVisibleResultCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);
        if (visibleResultCount < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(visibleResultCount), "可见结果行数至少为 1");
        }

        _rootPath = rootPath;
        _source = searchSource;
        VisibleResultCount = visibleResultCount;
    }

    /// <summary>当前查询文本（字符/退格编辑；BeginSearchAsync 同步宿主防抖后的最新值）。</summary>
    public string Query { get; private set; } = string.Empty;

    /// <summary>搜索进行中（BeginSearchAsync 入口置位/收尾复位 — 渲染层 spinner 帧字符 + 搜索中状态行）。</summary>
    public bool Searching { get; private set; }

    /// <summary>命中结果（只读 — 收集上限 MaxResults，Truncated 标记是否截断）。</summary>
    public IReadOnlyList<SearchMatch> Results { get; private set; } = [];

    /// <summary>选中索引（↑↓ 导航钳制边界；enter 消费为 SelectedMatch）。</summary>
    public int SelectedIndex { get; private set; }

    /// <summary>选中命中（enter 置位 — 宿主取走消费后调 Close 接管定位；查询编辑/新搜索开始时失效置空）。</summary>
    public SearchMatch? SelectedMatch { get; private set; }

    /// <summary>已关闭（esc 置位 — 宿主摘除组件；关闭后键路由一律不消费、渲染跳过）。</summary>
    public bool Closed { get; private set; }

    /// <summary>结果达上限截断标记（溢出行尾 "+" — 基准 truncated matches 语义）。</summary>
    public bool Truncated { get; private set; }

    /// <summary>可见结果行数（结果窗口高度 — 导航滚动与溢出行结算依据）。</summary>
    public int VisibleResultCount { get; }

    /// <summary>动画时钟（50ms 帧累计 — 宿主每帧广播注入；驱动 Searching spinner 帧相位，测试直接赋值验证确定性）。</summary>
    public long AnimTimeMs { get; set; }

    /// <summary>内容高度 = margin 1 + 边框 1 + 标题 1 + 查询 1 + 空 1 + 结果区
    ///（空结果 = 状态行 1 / 非空 = 可见行数 + 溢出 0/1）+ 空 1 + footer 1 = 7 + 可见行 + 溢出。</summary>
    public int MeasureHeight(int width) => 7 + VisibleResultRows + (HiddenCount > 0 ? 1 : 0);

    /// <summary>渲染 — Closed 跳过（宿主随即摘除）；按固定行序绘制，超宽内容由 WriteText rightBound 截断。</summary>
    public void Render(Screen screen, Rect frame, Theme theme)
    {
        if (Closed)
        {
            return;
        }

        var row = frame.Y + 1;
        MessageRenderer.RenderPermissionBorder(screen, RowFrame(frame, row++), theme);
        MessageRenderer.RenderModelTitle(screen, RowFrame(frame, row++), theme, TitleText);
        RenderQueryRow(screen, RowFrame(frame, row++));
        row++;
        if (Results.Count == 0)
        {
            RenderStatusRow(screen, RowFrame(frame, row++));
        }
        else
        {
            row = RenderResultRows(screen, frame, theme, row);
        }

        row++;
        RenderFooterRow(screen, RowFrame(frame, row));
    }

    /// <summary>键路由 — 消费返回 true（对话框活跃态的分派终结）；未消费返回 false（宿主继续
    /// 分派其他处理器）；Closed 后一律不消费（宿主尚未摘除期间的守卫）。</summary>
    /// <param name="key">解析后的按键事件。</param>
    /// <returns>是否消费。</returns>
    public bool TryHandleKey(KeyPress key)
    {
        if (Closed)
        {
            return false;
        }

        return key.Kind switch
        {
            KeyKind.Char when !key.Ctrl && !key.Meta && key.Input.Length > 0 => Consume(Type, key.Input),
            KeyKind.Backspace => Consume(Backspace),
            KeyKind.UpArrow => Consume(MoveUp),
            KeyKind.DownArrow => Consume(MoveDown),
            KeyKind.Return => Consume(Confirm),
            KeyKind.Escape => Consume(Close),
            _ => false,
        };
    }

    /// <summary>开始新搜索 — 同步查询（宿主防抖后的最新值）、清空旧结果/选中/截断标记；
    /// 流式收集（上限 MaxResults 停止 = 后端进程被 finally 杀树）；防抖取消为常态
    ///（OperationCanceledException + 令牌已取消 → 保留已收集部分静默收尾）；后端不可用/空查询
    /// 直接清场。组件不持运行中任务 — 宿主重入前先取消旧令牌。</summary>
    /// <param name="query">查询文本（宿主防抖后的最新值 — 同步为组件 Query 状态）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task BeginSearchAsync(string query, CancellationToken cancellationToken = default)
    {
        Query = query;
        SelectedIndex = 0;
        SelectedMatch = null;
        _windowStart = 0;
        Truncated = false;
        var collected = new List<SearchMatch>(capacity: 16);
        if (string.IsNullOrWhiteSpace(query) || _source is not { IsAvailable: true })
        {
            Results = [];
            Searching = false;
            return;
        }

        Searching = true;
        try
        {
            await foreach (var match in _source.ExecuteAsync(_rootPath, query, cancellationToken).ConfigureAwait(false))
            {
                collected.Add(match);
                if (collected.Count >= MaxResults)
                {
                    Truncated = true;
                    break;
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 防抖取消为常态 — 保留已收集部分结果静默收尾（源自身异常的 OCE 传播不吞）
        }
        finally
        {
            Results = collected;
            Searching = false;
        }
    }

    /// <summary>查询追字符（KeyKind.Char 路由 — 可打印输入；SelectedMatch 失效置空）。</summary>
    /// <param name="text">追击文本（单字符或粘贴段）。</param>
    public void Type(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        Query += text;
        SelectedMatch = null;
    }

    /// <summary>退格删末字符（代理对完整删除 — 尾前为高代理时删 2 单元防拆半产生非法字符串）。</summary>
    public void Backspace()
    {
        if (Query.Length == 0)
        {
            return;
        }

        var cut = Query.Length >= 2 && char.IsHighSurrogate(Query[^2]) ? 2 : 1;
        Query = Query[..^cut];
        SelectedMatch = null;
    }

    /// <summary>上移选中（顶部钳制 0；窗口跟随上滚）。</summary>
    public void MoveUp()
    {
        if (Results.Count == 0)
        {
            return;
        }

        SelectedIndex = Math.Max(0, SelectedIndex - 1);
        EnsureSelectedVisible();
    }

    /// <summary>下移选中（底部钳制末项；窗口跟随下滚）。</summary>
    public void MoveDown()
    {
        if (Results.Count == 0)
        {
            return;
        }

        SelectedIndex = Math.Min(Results.Count - 1, SelectedIndex + 1);
        EnsureSelectedVisible();
    }

    /// <summary>确认 — 置 SelectedMatch（Results 非空 = 当前选中项；空 = null 无操作语义）；
    /// 组件不自行关闭 — 宿主消费后调 Close 接管（定位/插入/外部编辑器打开）。</summary>
    public void Confirm() => SelectedMatch = Results.Count > 0 ? Results[SelectedIndex] : null;

    /// <summary>关闭 — Closed 置位（宿主随即摘除组件；幂等安全）。</summary>
    public void Close() => Closed = true;

    /// <summary>可见结果行数（空结果 = 状态行 1；非空 = min(可见行数, 结果数)）。</summary>
    private int VisibleResultRows => Results.Count == 0 ? 1 : Math.Min(VisibleResultCount, Results.Count);

    /// <summary>溢出数（结果总数超出可见行数的部分 — 溢出行 "… 还有 N 条" 数据源）。</summary>
    private int HiddenCount => Math.Max(0, Results.Count - VisibleResultCount);

    /// <summary>状态行文案 — 搜索中 / 空查询（输入以搜索…）/ 有查询无匹配 三分支（基准 emptyMessage 语义汉化）。</summary>
    private string StatusText => Searching
        ? StatusSearching
        : Query.Length == 0 ? StatusTypeToSearch : StatusNoMatches;

    /// <summary>结果行组渲染 — 可见窗口逐行（选中高亮）+ 溢出行；返回下一可用行号。</summary>
    private int RenderResultRows(Screen screen, Rect frame, Theme theme, int row)
    {
        var visible = Math.Min(VisibleResultCount, Results.Count);
        for (var i = 0; i < visible; i++)
        {
            var itemIndex = _windowStart + i;
            RenderResultRow(screen, RowFrame(frame, row++), theme, Results[itemIndex], itemIndex == SelectedIndex);
        }

        if (HiddenCount > 0)
        {
            RenderOverflowRow(screen, RowFrame(frame, row++));
        }

        return row;
    }

    /// <summary>查询行 — ❯ 前缀 + 查询回显 + Searching 时行尾 dim spinner 帧字符
    ///（AnimTimeMs 帧拍相位 — 复用 SpinnerMath 帧序列与全局 50ms 时钟同相位）。</summary>
    private void RenderQueryRow(Screen screen, Rect rowFrame)
    {
        screen.WriteText(rowFrame.Y, rowFrame.X, QueryPrompt, 0, rowFrame.Right);
        var col = screen.WriteText(rowFrame.Y, rowFrame.X + QueryPrompt.Length, Query, 0, rowFrame.Right);
        if (Searching)
        {
            var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
            var frame = SpinnerMath.SpinnerFrames[SpinnerMath.FrameOf(AnimTimeMs) % SpinnerMath.SpinnerFrames.Length];
            screen.WriteText(rowFrame.Y, col + 1, frame, dim, rowFrame.Right);
        }
    }

    /// <summary>状态行 — 1 空格缩进 dim（空结果时占据结果区首行）。</summary>
    private void RenderStatusRow(Screen screen, Rect rowFrame)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        screen.WriteText(rowFrame.Y, rowFrame.X, $" {StatusText}", dim, rowFrame.Right);
    }

    /// <summary>结果行 — 选中 = ❯ + suggestion 高亮；非选中 = 2 空格指示 + 默认样式；
    /// 文件:行号 dim + 空格 + 行文本（超宽由 WriteText rightBound 截断到框宽）。</summary>
    private void RenderResultRow(Screen screen, Rect rowFrame, Theme theme, SearchMatch match, bool isSelected)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        var selection = isSelected
            ? screen.Styles.GetOrAdd(new TextStyle { Foreground = theme.Suggestion })
            : 0;
        screen.WriteText(rowFrame.Y, rowFrame.X, isSelected ? $"{Figures.Pointer} " : "  ", isSelected ? selection : dim, rowFrame.Right);
        var col = screen.WriteText(rowFrame.Y, rowFrame.X + 2, $"{match.File}:{match.Line}", dim, rowFrame.Right);
        col = screen.WriteText(rowFrame.Y, col, " ", dim, rowFrame.Right);
        screen.WriteText(rowFrame.Y, col, match.Text, selection, rowFrame.Right);
    }

    /// <summary>溢出行 — 1 空格缩进 dim "… 还有 N 条"（Truncated 时尾缀 "+"，基准 truncated matches 语义）。</summary>
    private void RenderOverflowRow(Screen screen, Rect rowFrame)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        var suffix = Truncated ? "+" : string.Empty;
        screen.WriteText(rowFrame.Y, rowFrame.X, $" … 还有 {HiddenCount} 条{suffix}", dim, rowFrame.Right);
    }

    /// <summary>footer 行 — 1 空格缩进 dim 键位说明。</summary>
    private void RenderFooterRow(Screen screen, Rect rowFrame)
    {
        var dim = screen.Styles.GetOrAdd(new TextStyle { Dim = true });
        screen.WriteText(rowFrame.Y, rowFrame.X, $" {FooterText}", dim, rowFrame.Right);
    }

    /// <summary>窗口滚动跟随 — 选中项越出可见区上/下界时平移窗口，最终钳制在合法范围。</summary>
    private void EnsureSelectedVisible()
    {
        var visible = Math.Min(VisibleResultCount, Results.Count);
        if (visible <= 0)
        {
            _windowStart = 0;
            return;
        }

        if (SelectedIndex < _windowStart)
        {
            _windowStart = SelectedIndex;
        }
        else if (SelectedIndex >= _windowStart + visible)
        {
            _windowStart = SelectedIndex - visible + 1;
        }

        _windowStart = Math.Clamp(_windowStart, 0, Results.Count - visible);
    }

    /// <summary>无参动作消费包装（键路由 case 体统一 true 返回形态）。</summary>
    private static bool Consume(Action action)
    {
        action();
        return true;
    }

    /// <summary>带参动作消费包装（键路由 case 体统一 true 返回形态）。</summary>
    private static bool Consume(Action<string> action, string argument)
    {
        action(argument);
        return true;
    }

    /// <summary>单行 frame（Y 为该渲染行，高度 1）。</summary>
    private static Rect RowFrame(Rect frame, int row) => frame with { Y = row, Height = 1 };
}
