namespace NativeTui.Core.Repl;

/// <summary>
/// 选择粒度 — 双击/三击提升的选择单位（基准 anchorSpan.kind，selection.ts:26-31）。
/// </summary>
public enum SelectionGranularity
{
    /// <summary>字符粒度（默认 — 单击拖拽逐字符选择）。</summary>
    Character = 0,

    /// <summary>词粒度（双击 — 同类字符 run 扩展，selection.ts:240-254）。</summary>
    Word = 1,

    /// <summary>行粒度（三击及以上 — 整行，多击计数 cap 3，App.tsx:610-611）。</summary>
    Line = 2,
}

/// <summary>
/// 选区点 — 0-based 屏幕网格坐标（基准 Point { col, row }，selection.ts:17）。
/// </summary>
/// <param name="Col">列（0-based）。</param>
/// <param name="Row">行（0-based）。</param>
public readonly record struct SelectionPoint(int Col, int Row);

/// <summary>
/// 归一化选区边界 — Start 恒在 End 之前（阅读序：行优先、列次之），End 为 inclusive
/// 含端 cell（基准 selectionBounds，selection.ts:684-692）。
/// </summary>
/// <param name="Start">起点（阅读序在前）。</param>
/// <param name="End">终点（阅读序在后，含端 cell）。</param>
public readonly record struct SelectionBounds(SelectionPoint Start, SelectionPoint End);

/// <summary>
/// 行文本提供器委托（row → 该行全宽文本；越界行返回空 span）。
/// 类型修复 — 原形态 Func&lt;int, ReadOnlySpan&lt;char&gt;&gt; 编译非法：ref struct 作泛型类型
/// 实参要求泛型参数带 allows ref struct 反约束（C# 13 起规则，错误 CS9244 —
/// Func&lt;T,TResult&gt; 无该约束），改自定义 delegate 直持 ref struct 返回类型
///（方法签名成分合法 — API 语义不变，全部构造点 lambda/方法组隐式转换兼容）。
/// </summary>
/// <param name="row">0-based 屏幕行号。</param>
/// <returns>该行全宽文本（越界行返回空）。</returns>
public delegate ReadOnlySpan<char> SelectionLineAt(int row);

/// <summary>
/// 选区用屏幕投影 — 行文本提供器 + 网格尺寸（纯逻辑零 IO：终端事件与渲染由宿主接线）。
/// 行文本约定：定长 Cols 的行文本（Blank cell 投影为空格）、越界行返回空 span；
/// 列号 = 行文本索引（1 char = 1 列）。宽字符 SpacerTail 占位不反映在文本中为已知局限
/// （宽字符行内列号会偏移），接线层需要 cell 级精确时可升级为 cell 枚举接口。
/// </summary>
/// <param name="LineAt">行文本提供器（row → 该行全宽文本；越界行返回空）。</param>
/// <param name="Cols">屏幕列数。</param>
/// <param name="Rows">屏幕行数。</param>
public readonly record struct SelectionScreen(SelectionLineAt LineAt, int Cols, int Rows);

/// <summary>
/// 终端文本选区状态机 — 对齐基准 ink/selection.ts（流式线性选区：锚点 anchor + 活动点 focus
/// 两点定义，跨行 wrap，非矩形/block 语义 — selection.ts:4-7）：
/// <list type="bullet">
/// <item><b>单击拖拽</b>：Press 锚定（focus 延迟到首次真实拖动才置位 — 单击不产生选区，
/// selection.ts:86-88）；Drag 逐字符更新活动点（首次 motion 落在锚点同 cell 为 no-op 防抖动，
/// selection.ts:111-113）。</item>
/// <item><b>双击/三击</b>：Press 时判定多击（500ms 时间窗 + 1 cell 距离窗 — App.tsx:92-93、
/// 597-613）→ 词粒度（同类字符 run 扩展：空白=0 / 词字符=1 / 其它=2 三分类 — selection.ts:142-155）
/// 或行粒度（整行，selection.ts:368-380）；多击后 Drag 以锚定范围为根按词/行扩展
/// （目标与锚定范围三态比较：前向/反向/重叠 — selection.ts:389-421）。</item>
/// <item><b>shift+click 扩展</b>：锚点不动只改活动点，并降级字符粒度（对齐基准键盘
/// shift-extend 的 moveFocus 语义 — selection.ts:442-450；基准鼠标路径无 shift 分支，
/// 此为终端惯例补充）。无既有锚点时退化为普通锚定。</item>
/// <item><b>释放/取消</b>：Release 固化（isSelecting=false 但锚点/活动点与选区内容保留，
/// selection.ts:116-120）；Clear（Esc / 复制后）全清（selection.ts:122-134）；
/// 无 shift 的 Press 起新选区重新锚定（startSelection 语义）。</item>
/// <item><b>提取</b>：<see cref="GetSelectedText"/> 流式拼接 — 首行自起点列裁剪、中间行整行、
/// 尾行至终点列（selection.ts:784-788）+ 每行尾随空白裁剪（selection.ts:743）+ '\n' join。</item>
/// </list>
/// 时钟经构造注入（对齐 ReplShell.EscClock 先例 — 测试可控时间戳）；屏幕内容经
/// <see cref="SelectionScreen"/> 委托投影（快照语义：每次交互传入当时的屏幕状态），
/// 纯逻辑可测、与终端 IO 解耦。基准的 softWrap 回接与 drag-to-scroll 滚出累加器
/// （scrolledOffAbove/Below — selection.ts:32-48）不在本机职责内，由后续滚动集成任务接线。
/// </summary>
public sealed class SelectionState
{
    /// <summary>多击判定时间窗（ms）— 基准 MULTI_CLICK_TIMEOUT_MS（App.tsx:92）。</summary>
    public const long MultiClickTimeoutMs = 500;

    /// <summary>多击判定距离窗（cell 容差）— 基准 MULTI_CLICK_DISTANCE（App.tsx:93）。</summary>
    public const int MultiClickDistance = 1;

    /// <summary>时钟注入（默认墙钟；测试传可控时钟 — 对齐 ReplShell.EscClock 先例）。</summary>
    private readonly Func<long> _clock;

    /// <summary>
    /// 构造选区状态机。
    /// </summary>
    /// <param name="clock">时间戳源（ms；null = Environment.TickCount64 墙钟）。</param>
    public SelectionState(Func<long>? clock = null)
    {
        _clock = clock ?? (static () => Environment.TickCount64);
    }

    private (int Col, int Row, long Time)? _lastClick;
    private int _clickCount;
    private AnchorSpan? _anchorSpan;

    /// <summary>锚点（按下位置；无选区为 null — 基准 anchor，selection.ts:21）。</summary>
    public SelectionPoint? Anchor { get; private set; }

    /// <summary>活动点（当前拖拽位置；单击未拖动为 null — 基准 focus，selection.ts:23）。</summary>
    public SelectionPoint? Focus { get; private set; }

    /// <summary>拖拽进行中（Press 到 Release 之间 — 基准 isDragging，selection.ts:25）。</summary>
    public bool IsSelecting { get; private set; }

    /// <summary>选择粒度（词/行模式 = 多击锚定范围的种类，其余 Character — 基准 anchorSpan.kind 派生）。</summary>
    public SelectionGranularity Granularity => _anchorSpan?.Kind ?? SelectionGranularity.Character;

    /// <summary>是否存在选区（锚点与活动点齐备 — 基准 hasSelection，selection.ts:676-678）。</summary>
    public bool HasSelection => Anchor is not null && Focus is not null;

    /// <summary>归一化边界（Start ≤ End 阅读序；无选区为 null — 基准 selectionBounds，selection.ts:684-692）。</summary>
    public SelectionBounds? Bounds => NormalizeBounds();

    /// <summary>
    /// 鼠标事件路由 — Press（左键锚定/多击/shift 扩展）、Drag（左键拖拽扩展）、Release（固化）。
    /// 坐标为 0-based 屏幕网格坐标（终端 1-based → 网格 0-based 转换由宿主输入解析层完成）。
    /// 非左键 Press 断多击链（App.tsx:572-575）；Release 对任意键都固化（基准对非左键 release
    /// 仅在拖拽中生效 — App.tsx:625-637，Release 幂等故无条件分派等价）；滚轮/移动事件不属
    /// 选区（滚轮走键位系统 — App.tsx:543-545 注释），忽略。
    /// </summary>
    /// <param name="e">鼠标事件（SGR 解析后的 0-based 网格坐标）。</param>
    /// <param name="screen">屏幕投影（行文本提供器 + 尺寸 — 词/行粒度扫描用）。</param>
    public void HandleMouse(MouseEvent e, SelectionScreen screen)
    {
        switch (e.Kind)
        {
            case MouseKind.Press when e.Button == MouseButton.Left:
                Press(e.Col, e.Row, e.Shift, screen);
                break;
            case MouseKind.Press:
                _clickCount = 0;
                break;
            case MouseKind.Drag when e.Button == MouseButton.Left:
                Drag(e.Col, e.Row, screen);
                break;
            case MouseKind.Release:
                Release();
                break;
        }
    }

    /// <summary>
    /// 按下 — 左键 Press 原语（HandleMouse 的直接等价入口，供宿主自定义路由）。
    /// 序（对齐基准 App.tsx:542-622）：拖拽中的新 Press 先固化上一选区（丢 release 恢复，
    /// App.tsx:588-591）→ 更新多击链（时间窗+距离窗）→ shift 有锚时只改活动点（moveFocus 语义）
    /// → 多击消费（双击词/三击行，count cap 3 — App.tsx:610-612 + ink.tsx:1328-1340）
    /// → 单击锚定。
    /// </summary>
    /// <param name="col">按下列（0-based）。</param>
    /// <param name="row">按下行（0-based）。</param>
    /// <param name="shift">Shift 修饰位（true = 扩展既有选区而非重锚定）。</param>
    /// <param name="screen">屏幕投影（词/行粒度扫描用）。</param>
    public void Press(int col, int row, bool shift, SelectionScreen screen)
    {
        if (IsSelecting)
        {
            Release();
        }

        var now = _clock();
        var nearLast = _lastClick is { } last
            && now - last.Time < MultiClickTimeoutMs
            && Math.Abs(col - last.Col) <= MultiClickDistance
            && Math.Abs(row - last.Row) <= MultiClickDistance;
        _clickCount = nearLast ? _clickCount + 1 : 1;
        _lastClick = (col, row, now);

        if (shift && Anchor is not null)
        {
            Focus = new SelectionPoint(col, row);
            _anchorSpan = null;
            IsSelecting = true;
            return;
        }

        if (_clickCount >= 2)
        {
            if (_clickCount == 2)
            {
                SelectWordAt(col, row, screen);
            }
            else
            {
                SelectLineAt(col, row, screen);
            }

            return;
        }

        StartSelection(col, row);
    }

    /// <summary>
    /// 拖拽 — Drag 原语（活动点更新）。字符粒度精确到 cell（首次 motion 落在锚点同 cell 为
    /// no-op — 防 mode-1002 抖动把单击变成 1-cell 选区，selection.ts:111-113；focus 置位后
    /// 正常跟踪含回到锚点）；词/行粒度经 <see cref="ExtendSelection"/> snap 到词/行边界
    /// （ink.tsx:1348-1357 路由语义）。未拖拽中为 no-op（selection.ts:105）。
    /// </summary>
    /// <param name="col">当前列（0-based）。</param>
    /// <param name="row">当前行（0-based）。</param>
    /// <param name="screen">屏幕投影（词/行 snap 用）。</param>
    public void Drag(int col, int row, SelectionScreen screen)
    {
        if (!IsSelecting)
        {
            return;
        }

        if (_anchorSpan is { } span)
        {
            ExtendSelection(span, col, row, screen);
        }
        else
        {
            UpdateSelection(col, row);
        }
    }

    /// <summary>
    /// 释放 — 选区固化（isSelecting=false，锚点/活动点与选区内容保留供高亮与复制 —
    /// selection.ts:116-120）。多击链不重置（NSEvent clickCount 语义：release 后快速再击
    /// 仍续链 — App.tsx:638-644）。清空用 <see cref="Clear"/>。
    /// </summary>
    public void Release()
    {
        IsSelecting = false;
    }

    /// <summary>
    /// 清空 — Esc / 复制后取消选区（锚点/活动点/粒度全清 — 基准 clearSelection，
    /// selection.ts:122-134）。多击链保留（对齐基准：clearSelection 不动 App 侧计数）。
    /// </summary>
    public void Clear()
    {
        Anchor = null;
        Focus = null;
        IsSelecting = false;
        _anchorSpan = null;
    }

    /// <summary>
    /// cell 级选区判定 — 渲染层反白高亮消费面（基准 isCellSelected，selection.ts:698-710）：
    /// 行区间 [Start.Row, End.Row] 内，首行自 Start.Col 起、尾行至 End.Col 止（均含端）、
    /// 中间行全列 — 流式语义。
    /// </summary>
    /// <param name="row">判定行（0-based）。</param>
    /// <param name="col">判定列（0-based）。</param>
    /// <returns>该 cell 在选区内。</returns>
    public bool IsSelected(int row, int col)
    {
        if (NormalizeBounds() is not { } bounds)
        {
            return false;
        }

        if (row < bounds.Start.Row || row > bounds.End.Row)
        {
            return false;
        }

        if (row == bounds.Start.Row && col < bounds.Start.Col)
        {
            return false;
        }

        if (row == bounds.End.Row && col > bounds.End.Col)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// 提取选区文本 — 流式拼接（基准 getSelectedText，selection.ts:773-795）：按行序遍历
    /// [Start.Row, End.Row]，首行自 Start.Col 裁剪、中间行整行（0..Cols-1）、尾行至 End.Col；
    /// 行内按列裁剪；每行片段裁尾随空白（selection.ts:743 的 \s+$ 语义）；多行 join '\n'。
    /// 越界行/短行防御性 clamp 到实际文本长度。无选区返回空串。
    /// </summary>
    /// <param name="screen">屏幕投影（行文本源 + 尺寸）。</param>
    /// <returns>选区文本（跨行 '\n' join，行尾空白已裁）。</returns>
    public string GetSelectedText(SelectionScreen screen)
    {
        if (NormalizeBounds() is not { } bounds)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        for (var row = bounds.Start.Row; row <= bounds.End.Row; row++)
        {
            if (row > bounds.Start.Row)
            {
                builder.Append('\n');
            }

            var colStart = row == bounds.Start.Row ? bounds.Start.Col : 0;
            var colEnd = row == bounds.End.Row ? bounds.End.Col : screen.Cols - 1;
            AppendRowText(builder, screen.LineAt(row), colStart, colEnd);
        }

        return builder.ToString();
    }

    /// <summary>锚定 — 单击按下位置，focus 延迟到首次拖动（selection.ts:79-98）。</summary>
    private void StartSelection(int col, int row)
    {
        Anchor = new SelectionPoint(col, row);
        Focus = null;
        IsSelecting = true;
        _anchorSpan = null;
    }

    /// <summary>
    /// 双击词选 — 先锚定兜底（多击后拖拽仍可进行 — ink.tsx:1334），再同类字符 run 扩展
    /// （selection.ts:240-254）；词扫描无果（空行/越界）时 focus=anchor 兜底保证选区存在
    /// （ink.tsx:1338：防止 release 重新派发单击）。
    /// </summary>
    private void SelectWordAt(int col, int row, SelectionScreen screen)
    {
        StartSelection(col, row);
        if (WordBoundsAt(screen.LineAt(row), col, screen.Cols) is not { } word)
        {
            Focus = Anchor;
            return;
        }

        var lo = new SelectionPoint(word.Lo, row);
        var hi = new SelectionPoint(word.Hi, row);
        Anchor = lo;
        Focus = hi;
        _anchorSpan = new AnchorSpan(lo, hi, SelectionGranularity.Word);
    }

    /// <summary>
    /// 三击行选 — 先锚定兜底，行在界内则整行 [0, Cols-1]（selection.ts:368-380），
    /// 越界时 focus=anchor 兜底（selectLineAt 的越界 no-op + ink.tsx:1338 兜底合成）。
    /// </summary>
    private void SelectLineAt(int col, int row, SelectionScreen screen)
    {
        StartSelection(col, row);
        if (row < 0 || row >= screen.Rows)
        {
            Focus = Anchor;
            return;
        }

        var lo = new SelectionPoint(0, row);
        var hi = new SelectionPoint(screen.Cols - 1, row);
        Anchor = lo;
        Focus = hi;
        _anchorSpan = new AnchorSpan(lo, hi, SelectionGranularity.Line);
    }

    /// <summary>字符粒度活动点更新（selection.ts:100-114 — 含锚点同 cell no-op 防抖动）。</summary>
    private void UpdateSelection(int col, int row)
    {
        if (Focus is null && Anchor is { } anchor && anchor.Col == col && anchor.Row == row)
        {
            return;
        }

        Focus = new SelectionPoint(col, row);
    }

    /// <summary>
    /// 词/行粒度拖拽扩展 — 鼠标目标的词/行与锚定范围三态比较（selection.ts:389-421）：
    /// 目标整体在锚定范围之前 → 反向（锚点取范围高端、活动点取目标低端）；之后 → 正向
    /// （锚点取范围低端、活动点取目标高端）；重叠 → 恰选锚定范围本身。词粒度目标扫描无果
    /// （空行/越界）时回退原始 cell（selection.ts:401-402 — 拖进无内容区仍可扩展）；
    /// 行粒度目标行 clamp 到界内（selection.ts:404）。
    /// </summary>
    private void ExtendSelection(AnchorSpan span, int col, int row, SelectionScreen screen)
    {
        SelectionPoint targetLo;
        SelectionPoint targetHi;
        if (span.Kind == SelectionGranularity.Word)
        {
            var word = WordBoundsAt(screen.LineAt(row), col, screen.Cols);
            targetLo = new SelectionPoint(word?.Lo ?? col, row);
            targetHi = new SelectionPoint(word?.Hi ?? col, row);
        }
        else
        {
            if (screen.Rows <= 0)
            {
                return;
            }

            var clampedRow = Math.Clamp(row, 0, screen.Rows - 1);
            targetLo = new SelectionPoint(0, clampedRow);
            targetHi = new SelectionPoint(screen.Cols - 1, clampedRow);
        }

        if (ComparePoints(targetHi, span.Lo) < 0)
        {
            Anchor = span.Hi;
            Focus = targetLo;
        }
        else if (ComparePoints(targetLo, span.Hi) > 0)
        {
            Anchor = span.Lo;
            Focus = targetHi;
        }
        else
        {
            Anchor = span.Lo;
            Focus = span.Hi;
        }
    }

    /// <summary>
    /// 同类字符 run 边界 — 从 col 向两侧扩展到 class 变化处（基准 wordBoundsAt 的文本级简化，
    /// selection.ts:162-224：无 noSelect 位图/SpacerTail 步进 — 文本投影无 cell 宽度信息，
    /// 参见 <see cref="SelectionScreen"/> 的列号=索引约定）。col 越界或行不存在返回 null。
    /// </summary>
    private static (int Lo, int Hi)? WordBoundsAt(ReadOnlySpan<char> line, int col, int cols)
    {
        if (col < 0 || col >= cols || line.IsEmpty)
        {
            return null;
        }

        var cls = ClassAt(line, col);
        var lo = col;
        while (lo > 0 && ClassAt(line, lo - 1) == cls)
        {
            lo--;
        }

        var hi = col;
        while (hi < cols - 1 && ClassAt(line, hi + 1) == cls)
        {
            hi++;
        }

        return (lo, hi);
    }

    /// <summary>取列的字符类 — 越出文本长度视为空白（Blank cell 投影，selection.ts:152）。</summary>
    private static int ClassAt(ReadOnlySpan<char> line, int col) =>
        col < line.Length ? CharClass(line[col]) : 0;

    /// <summary>
    /// 字符三分类（基准 charClass，selection.ts:142-155）：空白=0；词字符=1（Unicode 字母
    /// \p{L} + 数字 \p{N} + iTerm2 默认词内标点集 _ / . - + ~ \ — selection.ts:141-142 的
    /// WORD_CHAR）；其它=2。同类 run 即词边界（双击 foo 选 foo、双击标点选标点 run、双击
    /// 空格选空白 run）。代理对（补充平面 emoji）按 UTF-16 code unit 判为类 2 — 文本级
    /// 已知局限，见 <see cref="SelectionScreen"/> 约定。
    /// </summary>
    private static int CharClass(char c)
    {
        if (c == ' ')
        {
            return 0;
        }

        if (char.IsLetter(c) || char.IsNumber(c) || c is '_' or '/' or '.' or '-' or '+' or '~' or '\\')
        {
            return 1;
        }

        return 2;
    }

    /// <summary>归一化边界 — 锚点/活动点按阅读序取小者为 Start（selection.ts:684-692）。</summary>
    private SelectionBounds? NormalizeBounds()
    {
        if (Anchor is not { } anchor || Focus is not { } focus)
        {
            return null;
        }

        return ComparePoints(anchor, focus) <= 0
            ? new SelectionBounds(anchor, focus)
            : new SelectionBounds(focus, anchor);
    }

    /// <summary>两点阅读序比较（行优先、列次之）— 基准 comparePoints（selection.ts:226-231）。</summary>
    private static int ComparePoints(SelectionPoint a, SelectionPoint b)
    {
        if (a.Row != b.Row)
        {
            return a.Row < b.Row ? -1 : 1;
        }

        if (a.Col != b.Col)
        {
            return a.Col < b.Col ? -1 : 1;
        }

        return 0;
    }

    /// <summary>
    /// 追加单行片段 — 列区间 [colStart, colEnd] 含端裁剪（越界/短行 clamp 到实际文本长度），
    /// 尾随空白裁剪（基准 extractRowText 的 \s+$ 逐行裁 — selection.ts:716-744；C# 语义 =
    /// char.IsWhiteSpace 循环裁尾）。
    /// </summary>
    private static void AppendRowText(StringBuilder builder, ReadOnlySpan<char> line, int colStart, int colEnd)
    {
        var start = Math.Clamp(colStart, 0, line.Length);
        var end = Math.Min(colEnd, line.Length - 1);
        if (end < start)
        {
            return;
        }

        var slice = line.Slice(start, end - start + 1);
        while (slice.Length > 0 && char.IsWhiteSpace(slice[slice.Length - 1]))
        {
            slice = slice[..^1];
        }

        builder.Append(slice);
    }

    /// <summary>词/行粒度的多击锚定范围（基准 anchorSpan — selection.ts:26-31）。</summary>
    /// <param name="Lo">范围起点。</param>
    /// <param name="Hi">范围终点（含端）。</param>
    /// <param name="Kind">粒度（决定拖拽扩展 snap 到词还是行边界）。</param>
    private readonly record struct AnchorSpan(SelectionPoint Lo, SelectionPoint Hi, SelectionGranularity Kind);
}
