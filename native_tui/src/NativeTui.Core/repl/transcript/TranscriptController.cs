namespace NativeTui.Core.Repl;

/// <summary>
/// transcript 全屏模式控制器 — 对齐基准全局 transcript 模式（REPL.tsx 4237-4294 +
/// ScrollKeybindingHandler.tsx modalPagerAction 900-959）：
/// <list type="bullet">
/// <item><b>Toggle</b>：进入 = 冻结快照（注入提供器拍摄 + 防御拷贝隔离 live 原地变更）+
/// 滚到底部（最新消息可见）+ verbose 回调 true；退出 = 快照丢弃恢复 live + 搜索重置 +
/// 滚动复位 + verbose 回调 false。</item>
/// <item><b>TryHandleKey</b>：less 键位路由 — j/k 逐行、g/G（含 kitty g+shift）首末、
/// ctrl+u/d 半屏、ctrl+b/f/空格/b 整屏、ctrl+n/p 行级（emacs 风格）、↑↓/home/end；
/// '/' 增量搜索 + n/N 循环跳转（uniform batch 每字符步进）；q/Esc 退出（ungated）。
/// 消费返回 true，未消费返回 false 由宿主继续分派其他处理器。</item>
/// <item><b>Tick</b>：帧拍累计激活时长（当前无动画 — 契约为后续扩展预留）。</item>
/// <item><b>滚动抽象</b>：以"行"为单位（ScrollOffset），与具体渲染解耦；视口行数由宿主
/// resize 时设置（半屏/整屏滚动量与 clamp 上界）。</item>
/// </list>
/// 键位分派接线（ctrl+o 捕获 → Toggle、verbose 回调 → ReplView.SetVerboseMode）由宿主
/// ReplShell 执行，本类不依赖渲染与终端。搜索文本源与快照数据源均经构造注入委托，
/// 保持纯逻辑可测。
/// </summary>
public sealed class TranscriptController
{
    /// <summary>默认视口行数（宿主设置前的占位 — 常见 24 行终端）。</summary>
    public const int DefaultViewportHeight = 24;

    private readonly Func<TranscriptSnapshot> _snapshotProvider;
    private readonly Action<bool> _onVerboseChanged;
    private int _searchAnchorOffset;

    /// <summary>构造 — 快照提供器（进入时调用一次拍摄冻结行集合与流式长度）；verbose 联动回调（Toggle 进出时通知，S6 接 ReplView.SetVerboseMode）。</summary>
    /// <param name="snapshotProvider">快照数据源（live 消息全文行投影 — 进入 transcript 时调用一次冻结）。</param>
    /// <param name="onVerboseChanged">verbose 联动回调（进入 true / 退出 false）。</param>
    public TranscriptController(Func<TranscriptSnapshot> snapshotProvider, Action<bool> onVerboseChanged)
    {
        _snapshotProvider = snapshotProvider;
        _onVerboseChanged = onVerboseChanged;
        Search = new TranscriptSearchState(() => Snapshot?.Lines ?? []);
    }

    /// <summary>transcript 模式是否激活。</summary>
    public bool IsActive { get; private set; }

    /// <summary>滚动行偏移（0-based 行单位 — RenderScrollOffset 语义，渲染消费与具体实现解耦）。</summary>
    public int ScrollOffset { get; private set; }

    /// <summary>视口行数（半屏/整屏滚动量与 clamp 上界 — 宿主 resize 时设置）。</summary>
    public int ViewportHeight { get; set; } = DefaultViewportHeight;

    /// <summary>冻结快照（激活时非 null；退出后 null = 恢复 live）。</summary>
    public TranscriptSnapshot? Snapshot { get; private set; }

    /// <summary>搜索状态（行源绑定当前快照 — 快照冻结后会话内不变；退出重置）。</summary>
    public TranscriptSearchState Search { get; }

    /// <summary>transcript 模式累计时长 ms（Tick 帧拍累计；退出清零）。</summary>
    public int UptimeMs { get; private set; }

    /// <summary>内容总行数（快照行数；未激活 0）。</summary>
    public int ContentLineCount => Snapshot?.LineCount ?? 0;

    /// <summary>最大滚动偏移（内容超出视口部分；不足一屏 = 0 不可滚）。</summary>
    public int MaxScrollOffset => Math.Max(0, ContentLineCount - ViewportHeight);

    /// <summary>进出切换 — 进入（冻结快照 + 滚到底部 + verbose true）/ 退出（快照丢弃 + 搜索重置 + verbose false）。幂等安全：重复调用自动翻转。</summary>
    public void Toggle()
    {
        if (IsActive)
        {
            ExitTranscript();
        }
        else
        {
            EnterTranscript();
        }
    }

    /// <summary>帧拍 — 累计激活时长（elapsedMs 毫秒；未激活或非正值丢弃）。当前无动画驱动，契约为后续滚动惯性等扩展预留。</summary>
    public void Tick(int elapsedMs)
    {
        if (!IsActive || elapsedMs <= 0)
        {
            return;
        }

        UptimeMs += elapsedMs;
    }

    /// <summary>键位路由 — 消费返回 true（滚动/搜索/退出已处理），未消费返回 false（宿主继续分派其他处理器，如 ctrl+o 切换、ctrl+c 中断）。</summary>
    public bool TryHandleKey(KeyPress key)
    {
        if (!IsActive)
        {
            return false;
        }

        return Search.BarOpen ? HandleSearchBarKey(key) : HandleBrowseKey(key);
    }

    /// <summary>footer 数据投影 — 搜索栏打开时透出查询与 cur/count 徽章数据；查询激活时标记 SearchActive（渲染层追加 n/N 提示）。</summary>
    public TranscriptFooterState BuildFooter() => new()
    {
        SearchQuery = Search.BarOpen ? Search.Query : null,
        SearchActive = Search.BarOpen || Search.Query.Length > 0,
        SearchCurrent = Search.CurrentOrdinal,
        SearchCount = Search.Count,
    };

    /// <summary>进入 — 拍摄冻结快照（防御拷贝隔离 live 原地变更）、滚到底部、通知 verbose 开。</summary>
    private void EnterTranscript()
    {
        IsActive = true;
        var live = _snapshotProvider();
        Snapshot = new TranscriptSnapshot([.. live.Lines], live.StreamingLength);
        UptimeMs = 0;
        ScrollTo(MaxScrollOffset);
        _onVerboseChanged(true);
    }

    /// <summary>退出 — 丢弃快照（恢复 live）、重置搜索（匹配绑定冻结行号，快照丢弃后必须清空防止错位）、复位滚动与时长、通知 verbose 关。</summary>
    private void ExitTranscript()
    {
        IsActive = false;
        Snapshot = null;
        Search.Reset();
        ScrollOffset = 0;
        UptimeMs = 0;
        _onVerboseChanged(false);
    }

    /// <summary>搜索栏键路由 — readline 编辑（对齐基准 useSearchInput）：可打印字符增量查询
    ///（每键就近跳转预览）、Backspace 删除、Return 提交（查询保留 — n/N 继续）、
    /// Esc/ctrl+c/ctrl+g 撤销并滚动回 '/' 打开时的锚点；其余（ctrl+o 等修饰组合、方向键）
    /// 不消费 fall through 宿主（对齐基准 useInput ctrl/meta return）。</summary>
    private bool HandleSearchBarKey(KeyPress key)
    {
        switch (key.Kind)
        {
            case KeyKind.Char when !key.Ctrl && !key.Meta:
                Search.Type(key.Input);
                JumpToNearestMatch();
                return true;
            case KeyKind.Backspace:
                Search.Backspace();
                JumpToNearestMatch();
                return true;
            case KeyKind.Return:
                Search.CommitBar();
                return true;
            case KeyKind.Escape:
                CancelSearch();
                return true;
            case KeyKind.Char when key.Ctrl && key.Input is "c" or "g":
                CancelSearch();
                return true;
            default:
                return false;
        }
    }

    /// <summary>浏览模式键路由 — 退出键（q/Esc，对齐基准 transcript:exit ungated）→
    /// 搜索入口（'/' 开栏 + 锚点捕获）→ n/N 跳转（uniform batch 每字符步进）→ less 滚动键位。</summary>
    private bool HandleBrowseKey(KeyPress key)
    {
        if (key.Kind == KeyKind.Escape)
        {
            ExitTranscript();
            return true;
        }

        if (IsPlainChar(key) && key.Input == "q")
        {
            ExitTranscript();
            return true;
        }

        if (IsPlainChar(key) && key.Input == "/")
        {
            _searchAnchorOffset = ScrollOffset;
            Search.OpenBar();
            return true;
        }

        if (IsPlainChar(key) && TryStepSearch(key.Input))
        {
            return true;
        }

        return TryScroll(key);
    }

    /// <summary>n/N 搜索跳转 — uniform batch 每字符步进一次（'nnn' = 3 步，对齐基准
    /// REPL.tsx 4280-4286 for 循环；混合批如 'nN' 不是键重复不处理）；跳转后匹配行对齐视口顶。</summary>
    private bool TryStepSearch(string input)
    {
        var c = input.Length > 0 ? input[0] : '\0';
        if ((c != 'n' && c != 'N') || Search.Count == 0 || !IsUniformRun(input, c))
        {
            return false;
        }

        for (var i = 0; i < input.Length; i++)
        {
            if (c == 'n')
            {
                Search.Next();
            }
            else
            {
                Search.Prev();
            }
        }

        ScrollToMatch();
        return true;
    }

    /// <summary>less 滚动键位 — 对齐基准 modalPagerAction（ScrollKeybindingHandler.tsx
    /// 900-959）：meta 不处理；无修饰方向键/Home/End；ctrl+u/d/b/f/n/p（u/d 半屏、b/f 整屏、
    /// n/p 行级 emacs 风格）；ctrl+shift 组合不处理（shift 保留给选择扩展）；裸字母见
    /// <see cref="TryScrollBareLetter"/>。uniform batch 消费为单次动作（对齐基准单 action 语义
    /// — g/G 幂等绝对跳转，次数无关）。</summary>
    private bool TryScroll(KeyPress key)
    {
        if (key.Meta)
        {
            return false;
        }

        if (!key.Ctrl && !key.Shift)
        {
            switch (key.Kind)
            {
                case KeyKind.UpArrow:
                    ScrollBy(-1);
                    return true;
                case KeyKind.DownArrow:
                    ScrollBy(1);
                    return true;
                case KeyKind.Home:
                    ScrollTo(0);
                    return true;
                case KeyKind.End:
                    ScrollTo(MaxScrollOffset);
                    return true;
            }
        }

        if (key.Ctrl)
        {
            if (key.Shift)
            {
                return false;
            }

            switch (key.Input)
            {
                case "u":
                    ScrollBy(-HalfPage());
                    return true;
                case "d":
                    ScrollBy(HalfPage());
                    return true;
                case "b":
                    ScrollBy(-FullPage());
                    return true;
                case "f":
                    ScrollBy(FullPage());
                    return true;
                case "n":
                    ScrollBy(1);
                    return true;
                case "p":
                    ScrollBy(-1);
                    return true;
                default:
                    return false;
            }
        }

        return TryScrollBareLetter(key);
    }

    /// <summary>裸字母滚动 — uniform run 才处理（'ggg' 是键重复，混合 'gG' 不是 — 基准 936）：
    /// G（legacy 大写）或 g+shift（kitty 协议大写）跳末尾（在 shift 门之前判 — 基准 937-939）；
    /// 其余 shift 组合不处理；g 首部、j/k 逐行、空格/b 整屏（less 原生翻页）。</summary>
    private bool TryScrollBareLetter(KeyPress key)
    {
        if (key.Kind != KeyKind.Char || key.Input.Length == 0)
        {
            return false;
        }

        var c = key.Input[0];
        if (!IsUniformRun(key.Input, c))
        {
            return false;
        }

        if (c == 'G' || (c == 'g' && key.Shift))
        {
            ScrollTo(MaxScrollOffset);
            return true;
        }

        if (key.Shift)
        {
            return false;
        }

        switch (c)
        {
            case 'g':
                ScrollTo(0);
                return true;
            case 'j':
                ScrollBy(1);
                return true;
            case 'k':
                ScrollBy(-1);
                return true;
            case ' ':
                ScrollBy(FullPage());
                return true;
            case 'b':
                ScrollBy(-FullPage());
                return true;
            default:
                return false;
        }
    }

    /// <summary>输入预览跳转 — 定位离当前视口最近的匹配（基准 nearest-ptr：锚 = 当前滚动行，
    /// 无后续匹配回绕首个）并对齐滚动到匹配行。</summary>
    private void JumpToNearestMatch()
    {
        if (Search.FocusNearest(ScrollOffset))
        {
            ScrollToMatch();
        }
    }

    /// <summary>滚动到当前匹配行（匹配行对齐视口顶 — less 跳转定位语义，clamp 内）。</summary>
    private void ScrollToMatch()
    {
        if (Search.Current is { } match)
        {
            ScrollTo(match.LineIndex);
        }
    }

    /// <summary>撤销搜索 — 清空查询并滚动回 '/' 打开时的锚点（pre-/ 状态恢复 — 基准 onCancel 语义）。</summary>
    private void CancelSearch()
    {
        Search.Cancel();
        ScrollTo(_searchAnchorOffset);
    }

    /// <summary>相对滚动（delta 行，正向下；clamp 到 [0, MaxScrollOffset]）。</summary>
    private void ScrollBy(int delta) => ScrollTo(ScrollOffset + delta);

    /// <summary>绝对滚动（clamp 到 [0, MaxScrollOffset] — 不足一屏不可滚）。</summary>
    private void ScrollTo(int offset) => ScrollOffset = Math.Clamp(offset, 0, MaxScrollOffset);

    /// <summary>半屏滚动量 — max(1, 视口行数/2)（基准 floor(viewportHeight/2)）。</summary>
    private int HalfPage() => Math.Max(1, ViewportHeight / 2);

    /// <summary>整屏滚动量 — max(1, 视口行数)。</summary>
    private int FullPage() => Math.Max(1, ViewportHeight);

    /// <summary>无修饰可打印字符（裸字母路由前置判断 — readline 之外的浏览模式字母键）。</summary>
    private static bool IsPlainChar(KeyPress key) =>
        key.Kind == KeyKind.Char && !key.Ctrl && !key.Meta;

    /// <summary>uniform 键重复批 — 输入为同一字符的重复串（'ggg'；混合如 'gG' 不是 — 基准 936）。调用方保证 input 非空且 c 为首字符。</summary>
    private static bool IsUniformRun(string input, char c) =>
        input.Length > 0 && input == new string(c, input.Length);
}
