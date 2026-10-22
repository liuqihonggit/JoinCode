namespace NativeTui.Tests.Repl;

/// <summary>
/// S12 四域对话框统一接线 — GlobalSearch 全局搜索域冒烟：
/// ctrl+shift+f 键位挂载 / rg 不可用打开即提示 / 字符进查询不进输入框 /
/// 防抖帧拍（300ms 静默 + DrainEngine Tick 派发 BeginSearchAsync，EscClock 可控时间戳）/
/// 防抖未到期不派发 / 查询幂等（一致不重复派发）/ 重入取消旧令牌 /
/// enter 确认插入 "@{文件}#L{行} " + 关闭 / esc 关闭终结令牌。
/// </summary>
public sealed class ReplShellDialogWiringGlobalSearchTests
{
    /// <summary>构造注入假搜索源的测试 shell（EscClock 可控 — 防抖确定性驱动）。</summary>
    private static (ReplShell Shell, FakeSearchSource Source) CreateShell(
        FakeSearchSource source,
        long clock = 1_000_000)
    {
        var shell = new ReplShell(new FakeReplEngine(), new BannerItem("1", "m", "b", "/tmp"), new StringWriter(),
            sizeSource: new FakeTerminalSizeSource(80, 24),
            workspaceRoot: "/tmp",
            searchSource: source);
        shell.EscClock = () => clock;
        shell.SeedContent();
        return (shell, source);
    }

    /// <summary>ctrl+shift+f 键（双通道归一后的统一形态）。</summary>
    private static KeyPress SearchTrigger() => new(KeyKind.Char, "f", Ctrl: true, Shift: true);

    /// <summary>模拟键入字符（对话框查询路由）。</summary>
    private static void Type(ReplShell shell, string text)
    {
        foreach (var c in text)
        {
            shell.HandleRawKeyPress(new KeyPress(KeyKind.Char, c.ToString()));
        }
    }

    /// <summary>ctrl+shift+f：对话框挂载滚动区（首查空查询 — 组件清场态静默等待）。</summary>
    [Fact]
    public void GlobalSearchTrigger_MountsDialog()
    {
        var (shell, source) = CreateShell(new FakeSearchSource());

        shell.HandleRawKeyPress(SearchTrigger());

        Assert.True(shell.IsGlobalSearchOpen);
        Assert.Contains(shell.View.Messages.Items, item => item is GlobalSearchDialog);
        // 挂载即静默 — 未派发任何搜索
        Assert.Empty(source.Queries);
    }

    /// <summary>rg 不可用（IsAvailable false）：打开即通知提示降级。</summary>
    [Fact]
    public void GlobalSearchTrigger_RgUnavailable_ShowsNotice()
    {
        var (shell, _) = CreateShell(new FakeSearchSource { IsAvailable = false });

        shell.HandleRawKeyPress(SearchTrigger());

        Assert.True(shell.IsGlobalSearchOpen);
        Assert.Equal(1, shell.View.Footer.Notifications!.QueuedCount);
    }

    /// <summary>无搜索源（null 注入）：打开即通知提示降级（对齐 rg 不可用同文案）。</summary>
    [Fact]
    public void GlobalSearchTrigger_NoSource_ShowsNotice()
    {
        var shell = new ReplShell(new FakeReplEngine(), new BannerItem("1", "m", "b", "/tmp"), new StringWriter(),
            sizeSource: new FakeTerminalSizeSource(80, 24),
            workspaceRoot: "/tmp");
        shell.SeedContent();

        shell.HandleRawKeyPress(SearchTrigger());

        Assert.True(shell.IsGlobalSearchOpen);
        Assert.Equal(1, shell.View.Footer.Notifications!.QueuedCount);
    }

    /// <summary>无工作区根：降级诱导提示不挂载。</summary>
    [Fact]
    public void GlobalSearchTrigger_NoWorkspaceRoot_ShowsDegradedFeedback()
    {
        var shell = new ReplShell(new FakeReplEngine(), new BannerItem("1", "m", "b", "/tmp"), new StringWriter(),
            sizeSource: new FakeTerminalSizeSource(80, 24),
            searchSource: new FakeSearchSource());
        shell.SeedContent();

        shell.HandleRawKeyPress(SearchTrigger());

        Assert.False(shell.IsGlobalSearchOpen);
        var feedback = shell.View.Messages.Items.OfType<ToolResultItem>().Single();
        Assert.Contains("工作区根", feedback.Summary);
    }

    /// <summary>字符进对话框查询不进输入框（模态吞键）。</summary>
    [Fact]
    public void GlobalSearchOpen_CharactersGoToQueryNotInput()
    {
        var (shell, _) = CreateShell(new FakeSearchSource());

        shell.HandleRawKeyPress(SearchTrigger());
        Type(shell, "abc");

        Assert.Equal(string.Empty, shell.Input.Text);
        Assert.Equal("abc", shell.GlobalSearchDialog!.Query);
    }

    /// <summary>防抖帧拍：查询静默未满 300ms 不派发；满 300ms 后 DrainEngine Tick 派发。</summary>
    [Fact]
    public async Task GlobalSearchDebounce_DispatchesAfter300MsSilence()
    {
        long clock = 1_000_000;
        var (shell, source) = CreateShell(new FakeSearchSource(new SearchMatch("src/a.cs", 10, "hit")), clock: clock);
        shell.EscClock = () => clock;
        shell.HandleRawKeyPress(SearchTrigger());

        Type(shell, "hit");
        shell.DrainEngine();
        Assert.Empty(source.Queries);

        clock += ReplShell.GlobalSearchDebounceMs - 1;
        shell.DrainEngine();
        Assert.Empty(source.Queries);

        clock += 1;
        shell.DrainEngine();
        await WaitForAsync(() => source.Queries.Count > 0);
        Assert.Equal(["hit"], source.Queries);
        await WaitForAsync(() => shell.GlobalSearchDialog!.Results.Count > 0);
        Assert.Equal("src/a.cs", shell.GlobalSearchDialog!.Results[0].File);
    }

    /// <summary>查询幂等：查询与已派发一致时（如导航键触发 Tick）不重复派发。</summary>
    [Fact]
    public async Task GlobalSearchDebounce_SameQuery_DoesNotRedispatch()
    {
        long clock = 1_000_000;
        var (shell, source) = CreateShell(new FakeSearchSource(), clock: clock);
        shell.EscClock = () => clock;
        shell.HandleRawKeyPress(SearchTrigger());

        Type(shell, "q");
        clock += ReplShell.GlobalSearchDebounceMs;
        shell.DrainEngine();
        await WaitForAsync(() => source.Queries.Count > 0);
        Assert.Single(source.Queries);

        // 无查询变化（时间戳未重置）— 再多拍也不重复派发
        clock += ReplShell.GlobalSearchDebounceMs * 3;
        shell.DrainEngine();
        Assert.Single(source.Queries);
    }

    /// <summary>防抖重入：查询变化再次派发时旧令牌先取消（组件不持运行中任务契约 — 宿主取消）。</summary>
    [Fact]
    public async Task GlobalSearchDebounce_Redispatch_CancelsOldToken()
    {
        long clock = 1_000_000;
        var (shell, source) = CreateShell(new FakeSearchSource(new SearchMatch("a.cs", 1, "x")), clock: clock);
        shell.EscClock = () => clock;
        shell.HandleRawKeyPress(SearchTrigger());

        Type(shell, "x");
        clock += ReplShell.GlobalSearchDebounceMs;
        shell.DrainEngine();
        await WaitForAsync(() => source.Tokens.Count > 0);
        var firstToken = source.Tokens[0];

        Type(shell, "y");
        clock += ReplShell.GlobalSearchDebounceMs;
        shell.DrainEngine();
        await WaitForAsync(() => source.Tokens.Count > 1);

        Assert.True(firstToken.IsCancellationRequested);
        Assert.False(source.Tokens[1].IsCancellationRequested);
        await WaitForAsync(() => source.Queries.Count > 1);
        Assert.Equal("xy", source.Queries[1]);
    }

    /// <summary>enter 确认选中：插入 "@{文件}#L{行} " 到输入框 + 通知反馈 + 对话框移除 + 令牌终结。</summary>
    [Fact]
    public async Task GlobalSearchConfirm_InsertsLineReferenceAndCloses()
    {
        long clock = 1_000_000;
        var (shell, source) = CreateShell(new FakeSearchSource(new SearchMatch("src/a.cs", 10, "hit")), clock: clock);
        shell.EscClock = () => clock;
        shell.HandleRawKeyPress(SearchTrigger());

        Type(shell, "hit");
        clock += ReplShell.GlobalSearchDebounceMs;
        shell.DrainEngine();
        await WaitForAsync(() => source.Tokens.Count > 0);
        var token = source.Tokens[0];

        shell.HandleRawKeyPress(new KeyPress(KeyKind.Return, string.Empty));

        Assert.False(shell.IsGlobalSearchOpen);
        Assert.DoesNotContain(shell.View.Messages.Items, item => item is GlobalSearchDialog);
        Assert.Equal("@src/a.cs#L10 ", shell.Input.Text);
        Assert.Equal(1, shell.View.Footer.Notifications!.QueuedCount);
        Assert.True(token.IsCancellationRequested);
    }

    /// <summary>esc 关闭：对话框移除 + 进行中令牌取消终结 + 输入框不受打扰。</summary>
    [Fact]
    public async Task GlobalSearchEscape_ClosesAndCancelsToken()
    {
        long clock = 1_000_000;
        var (shell, source) = CreateShell(new FakeSearchSource(new SearchMatch("a.cs", 1, "x")), clock: clock);
        shell.EscClock = () => clock;
        shell.HandleRawKeyPress(SearchTrigger());

        Type(shell, "x");
        clock += ReplShell.GlobalSearchDebounceMs;
        shell.DrainEngine();
        await WaitForAsync(() => source.Tokens.Count > 0);
        var token = source.Tokens[0];

        shell.HandleRawKeyPress(new KeyPress(KeyKind.Escape, string.Empty));

        Assert.False(shell.IsGlobalSearchOpen);
        Assert.True(token.IsCancellationRequested);
        Assert.Equal(string.Empty, shell.Input.Text);
    }

    /// <summary>轮询等待异步完成（fire-and-forget 搜索任务 — 16 次 × 25ms 收敛窗）。</summary>
    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var i = 0; i < 16 && !condition(); i++)
        {
            await Task.Delay(25);
        }

        Assert.True(condition(), "异步条件未在收敛窗内满足");
    }

    /// <summary>假搜索源 — 预设结果流注入（记录查询与令牌供防抖/取消断言）。</summary>
    private sealed class FakeSearchSource : ISearchSource
    {
        private readonly SearchMatch[] _matches;

        /// <summary>创建假源 — matches 预设流。</summary>
        public FakeSearchSource(params SearchMatch[] matches) => _matches = matches;

        /// <summary>可用标记（测试翻转验证 rg 不可用降级提示）。</summary>
        public bool IsAvailable { get; set; } = true;

        /// <summary>收到的查询记录（防抖透传断言）。</summary>
        public List<string> Queries { get; } = [];

        /// <summary>收到的取消令牌记录（重入取消断言）。</summary>
        public List<CancellationToken> Tokens { get; } = [];

        /// <summary>流式产出预设结果（令牌取消即中断传播）。</summary>
        public async IAsyncEnumerable<SearchMatch> ExecuteAsync(
            string rootPath,
            string query,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            Queries.Add(query);
            Tokens.Add(cancellationToken);
            foreach (var match in _matches)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await Task.Yield();
                yield return match;
            }
        }
    }
}
