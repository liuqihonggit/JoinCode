namespace NativeTui.Tests.Repl;

/// <summary>
/// 全局搜索对话框测试 — 状态机（查询编辑/导航钳制/窗口滚动/enter 暴露/esc 关闭/搜索收集
/// 截断/取消保留部分结果）+ 渲染（固定行序/状态行三分支/spinner 帧相位确定性/选中高亮/
/// 超宽截断/关闭跳过）。结果注入走假搜索源（ISearchSource 接缝 — 不跑真 rg，
/// 真 rg 集成见 GlobalSearchRipgrepIntegrationTests）。
/// </summary>
public sealed class GlobalSearchDialogTests
{
    /// <summary>行序基准常量 — 边框 1 / 标题 2 / 查询 3 / 空 4 / 结果区 5 起 / footer = 高度-1。</summary>
    private const int BorderRow = 1;

    private const int TitleRow = 2;
    private const int QueryRow = 3;
    private const int FirstResultRow = 5;

    /// <summary>线性命中工厂 — src/file{N}.ts:N "text N"（行号即区分位）。</summary>
    private static SearchMatch Match(int line) => new($"src/file{line}.ts", line, $"text {line}");

    /// <summary>显式命中工厂。</summary>
    private static SearchMatch Match(string file, int line, string text) => new(file, line, text);

    /// <summary>对话框工厂 — 搜索根取临时路径（非空即可，测试不落盘）。</summary>
    private static GlobalSearchDialog NewDialog(ISearchSource? source = null, int visible = GlobalSearchDialog.DefaultVisibleResultCount) =>
        new(Path.GetTempPath(), source, visible);

    /// <summary>线性命中序列工厂（1..count）。</summary>
    private static SearchMatch[] LinearMatches(int count) =>
        [.. Enumerable.Range(1, count).Select(Match)];

    /// <summary>查询编辑 — 字符追加/退格删除/代理对完整删除不拆半。</summary>
    [Fact]
    public void Type_Backspace_EditsQuery()
    {
        var dialog = NewDialog();
        Assert.Equal(string.Empty, dialog.Query);

        Assert.True(dialog.TryHandleKey(KeyPress.Char("a")));
        Assert.True(dialog.TryHandleKey(KeyPress.Char("b")));
        Assert.Equal("ab", dialog.Query);

        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.Backspace)));
        Assert.Equal("a", dialog.Query);

        dialog.TryHandleKey(KeyPress.Named(KeyKind.Backspace));
        Assert.Equal(string.Empty, dialog.Query);
    }

    /// <summary>代理对退格 — 高代理在尾前时删 2 单元（😀 一次退格清空）。</summary>
    [Fact]
    public void Backspace_RemovesSurrogatePairIntact()
    {
        var dialog = NewDialog();
        dialog.TryHandleKey(KeyPress.Char("\ud83d"));
        dialog.TryHandleKey(KeyPress.Char("\ude00"));
        Assert.Equal("\ud83d\ude00", dialog.Query);

        dialog.TryHandleKey(KeyPress.Named(KeyKind.Backspace));
        Assert.Equal(string.Empty, dialog.Query);
    }

    /// <summary>键路由面 — Char/Backspace/↑/↓/Return/Escape 消费 true；Tab 与 Ctrl+字符不消费（宿主继续分派）。</summary>
    [Fact]
    public void TryHandleKey_RoutesAndIgnores()
    {
        var dialog = NewDialog();
        Assert.True(dialog.TryHandleKey(KeyPress.Char("x")));
        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.Backspace)));
        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.UpArrow)));
        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.DownArrow)));
        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.Return)));
        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.Escape)));

        var untouched = NewDialog();
        Assert.False(untouched.TryHandleKey(KeyPress.Named(KeyKind.Tab)));
        Assert.False(untouched.TryHandleKey(new KeyPress(KeyKind.Char, "c", Ctrl: true)));
    }

    /// <summary>esc 关闭 — Closed 置位后键路由一律不消费（宿主摘除前的守卫）。</summary>
    [Fact]
    public void Escape_SetsClosed_AndStopsKeyRouting()
    {
        var dialog = NewDialog();
        Assert.False(dialog.Closed);

        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.Escape)));
        Assert.True(dialog.Closed);
        Assert.False(dialog.TryHandleKey(KeyPress.Char("a")));
        Assert.False(dialog.TryHandleKey(KeyPress.Named(KeyKind.Escape)));
    }

    /// <summary>enter 暴露 — 置 SelectedMatch 为当前选中项（组件不自行关闭，宿主消费后调 Close）；
    /// 空结果 enter 保持 null。</summary>
    [Fact]
    public async Task Enter_ExposesSelectedMatch_AndNullWhenEmpty()
    {
        var dialog = NewDialog(new FakeSearchSource([Match(1), Match(2), Match(3)]));
        await dialog.BeginSearchAsync("q");

        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.DownArrow)));
        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.DownArrow)));
        Assert.Equal(2, dialog.SelectedIndex);

        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.Return)));
        Assert.Equal(Match(3), dialog.SelectedMatch);
        Assert.False(dialog.Closed);

        var empty = NewDialog(new FakeSearchSource([]));
        await empty.BeginSearchAsync("q");
        Assert.True(empty.TryHandleKey(KeyPress.Named(KeyKind.Return)));
        Assert.Null(empty.SelectedMatch);
    }

    /// <summary>查询编辑使旧 SelectedMatch 失效置空（选择与查询同源失效）。</summary>
    [Fact]
    public async Task Typing_InvalidatesSelectedMatch()
    {
        var dialog = NewDialog(new FakeSearchSource([Match(1), Match(2)]));
        await dialog.BeginSearchAsync("q");

        dialog.TryHandleKey(KeyPress.Named(KeyKind.Return));
        Assert.NotNull(dialog.SelectedMatch);

        dialog.TryHandleKey(KeyPress.Char("x"));
        Assert.Null(dialog.SelectedMatch);
    }

    /// <summary>导航钳制 — 顶部上移不动 / 底部下移不动。</summary>
    [Fact]
    public async Task Navigation_ClampsAtBounds()
    {
        var dialog = NewDialog(new FakeSearchSource([Match(1), Match(2)]));
        await dialog.BeginSearchAsync("q");

        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.UpArrow)));
        Assert.Equal(0, dialog.SelectedIndex);

        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.DownArrow)));
        Assert.True(dialog.TryHandleKey(KeyPress.Named(KeyKind.DownArrow)));
        Assert.Equal(1, dialog.SelectedIndex);
    }

    /// <summary>窗口滚动 — 选中越出可见区时窗口平移（首行随之下移）；溢出行计数同步；
    /// MeasureHeight 随结果规模/溢出三档（8 状态行 / 10 三结果 / 16 溢出）。</summary>
    [Fact]
    public async Task Navigation_ScrollsWindow_OverflowRowAndHeight()
    {
        var dialog = NewDialog(new FakeSearchSource(LinearMatches(20)), visible: 8);
        await dialog.BeginSearchAsync("q");
        Assert.Equal(16, dialog.MeasureHeight(40));

        using var screen = new Screen(40, 16);
        dialog.Render(screen, new Rect(0, 0, 40, 16), Theme.Dark);
        // 初始焦点首项（基准 FuzzyPicker setFocusedIndex(0) 同语义 — SelectedIndex=0 高亮）
        Assert.Equal("❯ src/file1.ts:1 text 1", RowText.Of(screen, FirstResultRow, 0, 40).TrimEnd());
        Assert.Equal(" … 还有 12 条", RowText.Of(screen, FirstResultRow + 8, 0, 40).TrimEnd());

        for (var i = 0; i < 8; i++)
        {
            dialog.TryHandleKey(KeyPress.Named(KeyKind.DownArrow));
        }

        Assert.Equal(8, dialog.SelectedIndex);
        dialog.Render(screen, new Rect(0, 0, 40, 16), Theme.Dark);
        Assert.Equal("  src/file2.ts:2 text 2", RowText.Of(screen, FirstResultRow, 0, 40).TrimEnd());
        Assert.Equal("❯ src/file9.ts:9 text 9", RowText.Of(screen, FirstResultRow + 7, 0, 40).TrimEnd());
    }

    /// <summary>高度三档 — 空结果状态行 8 / 3 结果 10 / 溢出 16（Measure 与 Render 同参数防行数漂移）。</summary>
    [Fact]
    public async Task MeasureHeight_ThreeTiers()
    {
        Assert.Equal(8, NewDialog().MeasureHeight(40));

        var three = NewDialog(new FakeSearchSource([Match(1), Match(2), Match(3)]));
        await three.BeginSearchAsync("q");
        Assert.Equal(10, three.MeasureHeight(40));

        var twenty = NewDialog(new FakeSearchSource(LinearMatches(20)), visible: 8);
        await twenty.BeginSearchAsync("q");
        Assert.Equal(16, twenty.MeasureHeight(40));
    }

    /// <summary>搜索收集 — 流式全量收集 + 查询同步 + 收尾 Searching 复位。</summary>
    [Fact]
    public async Task BeginSearch_CollectsAndResetsSearching()
    {
        var source = new FakeSearchSource([Match(1), Match(2)]);
        var dialog = NewDialog(source);

        await dialog.BeginSearchAsync("needle");

        Assert.Equal("needle", dialog.Query);
        Assert.Equal(new[] { Match(1), Match(2) }, dialog.Results);
        Assert.False(dialog.Searching);
        Assert.False(dialog.Truncated);
        Assert.Equal(new[] { "needle" }, source.Queries);
    }

    /// <summary>上限截断 — 150 条源收集到 100 即停（后端流被弃置）+ Truncated 标记。</summary>
    [Fact]
    public async Task BeginSearch_TruncatesAtMaxResults()
    {
        var dialog = NewDialog(new FakeSearchSource(LinearMatches(150)));

        await dialog.BeginSearchAsync("q");

        Assert.Equal(GlobalSearchDialog.MaxResults, dialog.Results.Count);
        Assert.True(dialog.Truncated);
        Assert.False(dialog.Searching);
    }

    /// <summary>清场分支 — 空白查询 / 后端不可用 → 空结果 + Searching 复位（不触碰后端）。</summary>
    [Fact]
    public async Task BeginSearch_BlankQuery_OrUnavailable_Clears()
    {
        var source = new FakeSearchSource([Match(1)]);
        var dialog = NewDialog(source);

        await dialog.BeginSearchAsync("q");
        Assert.Single(dialog.Results);

        await dialog.BeginSearchAsync("   ");
        Assert.Empty(dialog.Results);
        Assert.False(dialog.Searching);

        source.IsAvailable = false;
        await dialog.BeginSearchAsync("q");
        Assert.Empty(dialog.Results);
        Assert.False(dialog.Searching);
    }

    /// <summary>防抖取消常态 — 源中断（OCE + 令牌已取消）保留已收集部分结果静默收尾。</summary>
    [Fact]
    public async Task BeginSearch_Cancellation_KeepsPartialResults()
    {
        var source = new FakeSearchSource(LinearMatches(10), cancelAfter: 3);
        var dialog = NewDialog(source);

        await dialog.BeginSearchAsync("q", new CancellationToken(canceled: true));

        Assert.Equal(3, dialog.Results.Count);
        Assert.False(dialog.Searching);
    }

    /// <summary>渲染固定行序 — 边框/标题/查询行/选中行高亮（❯ + file:line dim + text）/footer。</summary>
    [Fact]
    public async Task Render_FixedRows_TitleQuerySelectedRowFooter()
    {
        var dialog = NewDialog(new FakeSearchSource([Match("a.ts", 7, "const x")]));
        await dialog.BeginSearchAsync("abc");
        dialog.AnimTimeMs = 0;

        using var screen = new Screen(40, 8);
        Assert.Equal(8, dialog.MeasureHeight(40));
        dialog.Render(screen, new Rect(0, 0, 40, 8), Theme.Dark);

        Assert.Equal(new string('─', 40), RowText.Of(screen, BorderRow, 0, 40));
        Assert.Equal($" {GlobalSearchDialog.TitleText}", RowText.Of(screen, TitleRow, 0, 40).TrimEnd());
        Assert.Equal(" ❯ abc", RowText.Of(screen, QueryRow, 0, 40).TrimEnd());
        Assert.Equal("❯ a.ts:7 const x", RowText.Of(screen, FirstResultRow, 0, 40).TrimEnd());
        Assert.Equal($" {GlobalSearchDialog.FooterText}", RowText.Of(screen, 7, 0, 40).TrimEnd());
    }

    /// <summary>非选中行形态 — 2 空格指示 + file:line + text（默认样式无高亮）。</summary>
    [Fact]
    public async Task Render_UnselectedRow_TwoSpaceIndicator()
    {
        var dialog = NewDialog(new FakeSearchSource([Match("a.ts", 1, "first"), Match("b.ts", 2, "second")]));
        await dialog.BeginSearchAsync("q");

        using var screen = new Screen(40, 9);
        dialog.Render(screen, new Rect(0, 0, 40, 9), Theme.Dark);

        Assert.Equal("❯ a.ts:1 first", RowText.Of(screen, FirstResultRow, 0, 40).TrimEnd());
        Assert.Equal("  b.ts:2 second", RowText.Of(screen, FirstResultRow + 1, 0, 40).TrimEnd());
    }

    /// <summary>超宽截断 — 行文本超框宽由 rightBound 截断（40 列整，尾部不越界）。</summary>
    [Fact]
    public async Task Render_LongLine_TruncatedToFrameWidth()
    {
        var longText = new string('x', 60);
        var dialog = NewDialog(new FakeSearchSource([Match("a.ts", 1, longText)]));
        await dialog.BeginSearchAsync("q");

        using var screen = new Screen(40, 8);
        dialog.Render(screen, new Rect(0, 0, 40, 8), Theme.Dark);

        var rowText = RowText.Of(screen, FirstResultRow, 0, 40);
        Assert.Equal(40, rowText.Length);
        Assert.StartsWith("❯ a.ts:1 ", rowText);
        Assert.EndsWith("x", rowText);
    }

    /// <summary>Searching spinner 帧相位确定性 — AnimTimeMs 0 → "·"、120 → "✢"
    ///（SpinnerMath 12 帧序列 120ms/帧）；门放行后 Searching 复位、spinner 消失。</summary>
    [Fact]
    public async Task Render_SearchingSpinner_AdvancesWithAnimTime()
    {
        var gate = new TaskCompletionSource();
        var dialog = NewDialog(new FakeSearchSource([Match(1)]) { CompletionGate = gate });
        var searchTask = dialog.BeginSearchAsync("q");

        Assert.True(dialog.Searching);
        AssertSpinnerFrame(dialog, "·", withAnimTimeMs: 0);
        AssertSpinnerFrame(dialog, "✢", withAnimTimeMs: 120);

        gate.SetResult();
        await searchTask;
        Assert.False(dialog.Searching);
        AssertSpinnerFrame(dialog, null, withAnimTimeMs: 240);

        /// <summary>局部断言 — Searching 查询行行尾帧字符（null = 无搜索中不画）。</summary>
        static void AssertSpinnerFrame(GlobalSearchDialog dialog, string? expected, long withAnimTimeMs)
        {
            dialog.AnimTimeMs = withAnimTimeMs;
            using var screen = new Screen(40, 8);
            dialog.Render(screen, new Rect(0, 0, 40, 8), Theme.Dark);
            var queryRow = RowText.Of(screen, QueryRow, 0, 40).TrimEnd();
            Assert.Equal(expected is null ? " ❯ q" : $" ❯ q {expected}", queryRow);
        }
    }

    /// <summary>状态行三分支 — 空查询"输入以搜索…" / 有查询无匹配"无匹配" / 搜索中"搜索中…"。</summary>
    [Fact]
    public async Task Render_StatusRow_ThreeBranches()
    {
        var dialog = NewDialog();
        AssertStatusRow(dialog, " 输入以搜索…");

        dialog.TryHandleKey(KeyPress.Char("q"));
        AssertStatusRow(dialog, " 无匹配");

        var gate = new TaskCompletionSource();
        var searching = NewDialog(new FakeSearchSource([Match(1)]) { CompletionGate = gate });
        var searchTask = searching.BeginSearchAsync("held");
        AssertStatusRow(searching, " 搜索中…");

        gate.SetResult();
        await searchTask;

        /// <summary>局部断言 — 空结果时结果区首行为 dim 状态行。</summary>
        static void AssertStatusRow(GlobalSearchDialog dialog, string expected)
        {
            using var screen = new Screen(40, 8);
            dialog.Render(screen, new Rect(0, 0, 40, 8), Theme.Dark);
            Assert.Equal(expected, RowText.Of(screen, FirstResultRow, 0, 40).TrimEnd());
        }
    }

    /// <summary>关闭跳过渲染 — Closed 后全部行为空白（宿主随即摘除前的过渡守卫）。</summary>
    [Fact]
    public async Task Render_Closed_SkipsAllRows()
    {
        var dialog = NewDialog(new FakeSearchSource([Match(1)]));
        await dialog.BeginSearchAsync("q");
        dialog.Close();

        using var screen = new Screen(40, 8);
        dialog.Render(screen, new Rect(0, 0, 40, 8), Theme.Dark);
        Assert.Equal(string.Empty, RowText.Of(screen, BorderRow, 0, 40).TrimEnd());
        Assert.Equal(string.Empty, RowText.Of(screen, FirstResultRow, 0, 40).TrimEnd());
    }

    /// <summary>可见行数参数校验 — 0 拒绝（窗口语义至少 1 行）。</summary>
    [Fact]
    public void Constructor_RejectsNonPositiveVisibleCount() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => NewDialog(visible: 0));

    /// <summary>
    /// 假搜索源 — 预设结果流注入（记录查询供断言）；cancelAfter 达到即抛 OCE 模拟防抖中断；
    /// CompletionGate 非空时首条产出前挂起（驱动 Searching=true 中间态渲染断言，禁 Task.Delay）。
    /// </summary>
    private sealed class FakeSearchSource : ISearchSource
    {
        private readonly SearchMatch[] _matches;

        private readonly int _cancelAfter;

        /// <summary>挂起门（非空 = 每条产出前等待 — 搜索中状态确定性观测）。</summary>
        public TaskCompletionSource? CompletionGate { get; init; }

        /// <summary>创建假源 — matches 预设流；cancelAfter 第 N 条前抛 OCE（-1 = 不中断）。</summary>
        public FakeSearchSource(SearchMatch[] matches, int cancelAfter = -1)
        {
            _matches = matches;
            _cancelAfter = cancelAfter;
        }

        /// <summary>可用标记（测试翻转验证降级清场分支）。</summary>
        public bool IsAvailable { get; set; } = true;

        /// <summary>收到的查询记录（防抖透传断言）。</summary>
        public List<string> Queries { get; } = [];

        /// <summary>流式产出预设结果（CompletionGate 挂起/cancelAfter 中断注入）。</summary>
        public async IAsyncEnumerable<SearchMatch> ExecuteAsync(
            string rootPath,
            string query,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            var index = 0;
            foreach (var match in _matches)
            {
                if (index == _cancelAfter)
                {
                    throw new OperationCanceledException(cancellationToken);
                }

                if (CompletionGate is not null)
                {
                    await CompletionGate.Task;
                }

                yield return match;
                index++;
            }
        }
    }
}
