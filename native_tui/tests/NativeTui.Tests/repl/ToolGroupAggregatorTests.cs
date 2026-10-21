namespace NativeTui.Tests.Repl;

/// <summary>
/// 工具分组聚合状态机测试（S2 工具折叠渲染域）— ToolGroupAggregator 纯组件：
/// 分类计数（Read 读取 / Grep+Glob 搜索）与摘要文本；计数单调不回退（max-ref 防回抖 —
/// OnToolResult 后计数保持）；hint 派生（Read 路径 / Grep 引号 pattern）；
/// 700ms 防闪烁（帧拍时间戳结算 — 首个 hint 立即显示、后续每个至少展示 700ms 才允许切换、
/// 同值不重置窗口、OnFrame 幂等）；Accepts 收录口径（Read/Grep/Glob）。
/// </summary>
public sealed class ToolGroupAggregatorTests
{
    /// <summary>构造 Read 调用 JSON 参数。</summary>
    private static string ReadArgs(string path) => $$"""{"file_path":"{{path}}"}""";

    /// <summary>构造 Grep 调用 JSON 参数。</summary>
    private static string GrepArgs(string pattern, string path = "src") =>
        $$"""{"pattern":"{{pattern}}","path":"{{path}}"}""";

    [Fact]
    public void Counts_ByKind_SummaryTextJoinsParts()
    {
        // Read×2 + Grep×1 → "读取 2 个文件 · 搜索 1 个模式"（基准 Read N files, searched M patterns 汉化）
        var aggregator = new ToolGroupAggregator();
        aggregator.OnToolCall("Read", ReadArgs("a.cs"));
        aggregator.OnToolCall("Read", ReadArgs("b.cs"));
        aggregator.OnToolCall("Grep", GrepArgs("TODO"));

        Assert.Equal(2, aggregator.ReadCount);
        Assert.Equal(1, aggregator.SearchCount);
        Assert.Equal("读取 2 个文件 · 搜索 1 个模式", aggregator.SummaryText);
    }

    [Fact]
    public void Counts_OnlyRead_SummarySinglePart()
    {
        var aggregator = new ToolGroupAggregator();
        aggregator.OnToolCall("Read", ReadArgs("a.cs"));

        Assert.Equal("读取 1 个文件", aggregator.SummaryText);
    }

    [Fact]
    public void Counts_Zero_EmptySummary()
    {
        Assert.Equal(string.Empty, new ToolGroupAggregator().SummaryText);
    }

    [Fact]
    public void Counts_NeverDecrease_AfterResultsArrive()
    {
        // max-ref 防回抖 — 结果到达后聚合计数保持不回落（单调不回退语义）
        var aggregator = new ToolGroupAggregator();
        aggregator.OnToolCall("Read", ReadArgs("a.cs"));
        aggregator.OnToolResult();
        aggregator.OnToolCall("Grep", GrepArgs("x"));

        aggregator.OnToolResult();

        Assert.Equal(1, aggregator.ReadCount);
        Assert.Equal(1, aggregator.SearchCount);
        Assert.Equal("读取 1 个文件 · 搜索 1 个模式", aggregator.SummaryText);
    }

    [Fact]
    public void HasPendingToolCall_SetByCall_ClearedByResult()
    {
        var aggregator = new ToolGroupAggregator();

        Assert.False(aggregator.HasPendingToolCall);
        aggregator.OnToolCall("Read", ReadArgs("a.cs"));
        Assert.True(aggregator.HasPendingToolCall);
        aggregator.OnToolResult();
        Assert.False(aggregator.HasPendingToolCall);
    }

    [Fact]
    public void Accepts_ReadGrepGlobOnly()
    {
        Assert.True(ToolGroupAggregator.Accepts("Read"));
        Assert.True(ToolGroupAggregator.Accepts("Grep"));
        Assert.True(ToolGroupAggregator.Accepts("Glob"));
        Assert.False(ToolGroupAggregator.Accepts("Bash"));
        Assert.False(ToolGroupAggregator.Accepts("Edit"));
    }

    [Fact]
    public void Hint_FirstValue_ShownImmediately()
    {
        // 首个 hint 立即显示（基准 lastShownAtRef=0 墙钟语义 — 无需等待 700ms）
        var aggregator = new ToolGroupAggregator();
        aggregator.OnToolCall("Read", ReadArgs("a.cs"));

        aggregator.OnFrame(50);

        Assert.Equal("a.cs", aggregator.HintText);
    }

    [Fact]
    public void Hint_SecondValue_HeldUntil700MsElapsed()
    {
        // 700ms 防闪烁 — 新 hint 到达后旧 hint 至少展示 700ms 才允许切换（帧拍时间戳，无 Task.Delay）
        var aggregator = new ToolGroupAggregator();
        aggregator.OnToolCall("Read", ReadArgs("a.cs"));
        aggregator.OnFrame(50);
        aggregator.OnToolCall("Read", ReadArgs("b.cs"));

        aggregator.OnFrame(650);
        Assert.Equal("a.cs", aggregator.HintText);

        aggregator.OnFrame(700);
        Assert.Equal("a.cs", aggregator.HintText);

        aggregator.OnFrame(750);
        Assert.Equal("b.cs", aggregator.HintText);
    }

    [Fact]
    public void Hint_SameValue_DoesNotResetWindow()
    {
        // 同值重复到达 — 不重置展示窗口（600ms 后新值仍可立即接管）
        var aggregator = new ToolGroupAggregator();
        aggregator.OnToolCall("Read", ReadArgs("a.cs"));
        aggregator.OnFrame(50);
        aggregator.OnToolCall("Read", ReadArgs("a.cs"));

        aggregator.OnFrame(100);

        aggregator.OnToolCall("Read", ReadArgs("b.cs"));
        aggregator.OnFrame(751);
        Assert.Equal("b.cs", aggregator.HintText);
    }

    [Fact]
    public void Hint_KeptAfterResult_LatestStays()
    {
        // 结果到达后 hint 保留最新值继续展示（基准 latestDisplayHint 定格语义）
        var aggregator = new ToolGroupAggregator();
        aggregator.OnToolCall("Read", ReadArgs("a.cs"));
        aggregator.OnFrame(50);
        aggregator.OnToolResult();

        aggregator.OnFrame(2000);

        Assert.Equal("a.cs", aggregator.HintText);
    }

    [Fact]
    public void Hint_GrepPattern_Quoted()
    {
        // Grep hint — 引号包裹 pattern（基准 L196 lastSearch = `"${raw}"`）
        var aggregator = new ToolGroupAggregator();
        aggregator.OnToolCall("Grep", GrepArgs("TODO"));

        aggregator.OnFrame(50);

        Assert.Equal("\"TODO\"", aggregator.HintText);
    }

    [Fact]
    public void Hint_GlobPattern_Quoted_PlainTextFallsBackRaw()
    {
        // Glob hint 同 Grep 引号形态；纯文本参数降级原文无引号（模拟引擎场景）
        var aggregator = new ToolGroupAggregator();
        aggregator.OnToolCall("Glob", """{"pattern":"**/*.cs"}""");
        aggregator.OnFrame(50);
        Assert.Equal("\"**/*.cs\"", aggregator.HintText);

        aggregator.OnToolResult();
        aggregator.OnToolCall("Read", "f.cs");
        aggregator.OnFrame(10_000);
        Assert.Equal("f.cs", aggregator.HintText);
    }

    [Fact]
    public void OnFrame_Idempotent_WithinSameTimestamp()
    {
        // 幂等 — ScrollBox 测量/渲染两阶段同帧重复调用无副作用
        var aggregator = new ToolGroupAggregator();
        aggregator.OnToolCall("Read", ReadArgs("a.cs"));
        aggregator.OnFrame(50);
        aggregator.OnToolCall("Read", ReadArgs("b.cs"));

        aggregator.OnFrame(700);
        aggregator.OnFrame(700);

        Assert.Equal("a.cs", aggregator.HintText);
        aggregator.OnFrame(750);
        Assert.Equal("b.cs", aggregator.HintText);
    }
}
