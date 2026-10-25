namespace NativeTui.Tests.Repl.Transcript;

/// <summary>
/// TranscriptController 键位路由全表测试 — 对齐基准 modalPagerAction
///（ScrollKeybindingHandler.tsx 900-959）+ REPL.tsx 4266-4294：less 滚动键位
///（j/k 逐行、g/G 首末含 kitty 形态、ctrl+u/d 半屏、ctrl+b/f/空格/b 整屏、
/// ctrl+n/p 行级、↑↓/home/end）、uniform batch 语义（g 幂等、j 单 action、n 每字符步进）、
/// 未消费键 fall through、q/Esc 退出、搜索栏路由（readline 编辑 + ctrl+o 穿透）、
/// n/N 跳转滚动联动。100 行 · 视口 10（Max=90，进入后底部起步）。
/// </summary>
public sealed class TranscriptControllerKeyRoutingTests
{
    /// <summary>构造控制器并进入 transcript（默认 100 行 · 视口 10 — 进入后 ScrollOffset=90）。</summary>
    private static TranscriptController EnterController(int lineCount = 100, int viewport = 10)
    {
        var lines = Enumerable.Range(0, lineCount).Select(i => $"L{i:000}").ToArray();
        return EnterController(lines, viewport);
    }

    /// <summary>构造控制器并进入 transcript（自定义行集 · 视口 10 — 进入后滚到底部）。</summary>
    private static TranscriptController EnterController(string[] lines, int viewport = 10)
    {
        var controller = new TranscriptController(() => new TranscriptSnapshot(lines, 0), _ => { })
        {
            ViewportHeight = viewport,
        };
        controller.Toggle();
        return controller;
    }

    /// <summary>编码键 → 按键事件："j"/"G"/"ggg" 裸字符、"ctrl+u"/"meta+j" 修饰组合、
    /// "up"/"down"/"home"/"end"/"pageup"/"pagedown" 命名键、"space" 空格、"kitty-G" g+shift、"shift-j"。</summary>
    private static KeyPress Parse(string code) => code switch
    {
        "up" => KeyPress.Named(KeyKind.UpArrow),
        "down" => KeyPress.Named(KeyKind.DownArrow),
        "home" => KeyPress.Named(KeyKind.Home),
        "end" => KeyPress.Named(KeyKind.End),
        "pageup" => KeyPress.Named(KeyKind.PageUp),
        "pagedown" => KeyPress.Named(KeyKind.PageDown),
        "space" => new KeyPress(KeyKind.Char, " "),
        "kitty-G" => new KeyPress(KeyKind.Char, "g", Shift: true),
        "shift-j" => new KeyPress(KeyKind.Char, "j", Shift: true),
        _ when code.StartsWith("ctrl+", StringComparison.Ordinal) =>
            new KeyPress(KeyKind.Char, code["ctrl+".Length..], Ctrl: true),
        _ when code.StartsWith("meta+", StringComparison.Ordinal) =>
            new KeyPress(KeyKind.Char, code["meta+".Length..], Meta: true),
        _ => new KeyPress(KeyKind.Char, code),
    };

    /// <summary>进入 — 滚到底部（最新消息可见，Max=90）。</summary>
    [Fact]
    public void Enter_StartsAtBottom()
    {
        var controller = EnterController();
        Assert.True(controller.IsActive);
        Assert.Equal(90, controller.ScrollOffset);
    }

    /// <summary>从底部（90）出发的单键滚动量全表。</summary>
    [Theory]
    [InlineData("g", 0)]
    [InlineData("k", 89)]
    [InlineData("up", 89)]
    [InlineData("ctrl+u", 85)]
    [InlineData("ctrl+b", 80)]
    [InlineData("b", 80)]
    [InlineData("ctrl+p", 89)]
    [InlineData("home", 0)]
    [InlineData("j", 90)]
    [InlineData("down", 90)]
    [InlineData("G", 90)]
    [InlineData("kitty-G", 90)]
    [InlineData("end", 90)]
    [InlineData("ctrl+d", 90)]
    [InlineData("ctrl+f", 90)]
    [InlineData("ctrl+n", 90)]
    [InlineData("space", 90)]
    public void Scroll_FromBottom(string code, int expected)
    {
        var controller = EnterController();
        Assert.True(controller.TryHandleKey(Parse(code)));
        Assert.Equal(expected, controller.ScrollOffset);
    }

    /// <summary>从顶部（g 后 0）出发的单键滚动量全表。</summary>
    [Theory]
    [InlineData("j", 1)]
    [InlineData("down", 1)]
    [InlineData("ctrl+n", 1)]
    [InlineData("ctrl+d", 5)]
    [InlineData("ctrl+f", 10)]
    [InlineData("space", 10)]
    [InlineData("G", 90)]
    [InlineData("kitty-G", 90)]
    [InlineData("end", 90)]
    [InlineData("k", 0)]
    [InlineData("up", 0)]
    [InlineData("ctrl+u", 0)]
    [InlineData("ctrl+b", 0)]
    [InlineData("b", 0)]
    [InlineData("ctrl+p", 0)]
    [InlineData("home", 0)]
    public void Scroll_FromTop(string code, int expected)
    {
        var controller = EnterController();
        Assert.True(controller.TryHandleKey(Parse("g")));
        Assert.True(controller.TryHandleKey(Parse(code)));
        Assert.Equal(expected, controller.ScrollOffset);
    }

    /// <summary>不足一屏的内容 — Max=0 不可滚（所有滚动 clamp 到 0）。</summary>
    [Fact]
    public void Scroll_ContentShorterThanViewport_ClampsToZero()
    {
        var controller = EnterController(lineCount: 5, viewport: 10);
        Assert.Equal(0, controller.MaxScrollOffset);
        Assert.True(controller.TryHandleKey(Parse("G")));
        Assert.Equal(0, controller.ScrollOffset);
    }

    /// <summary>视口奇数行 — 半屏 = floor(视口/2)（视口 11 → 底部 89，半屏 5 → 84）。</summary>
    [Fact]
    public void Scroll_HalfPage_FloorsOddViewport()
    {
        var controller = EnterController(viewport: 11);
        Assert.True(controller.TryHandleKey(Parse("ctrl+u")));
        Assert.Equal(84, controller.ScrollOffset);
    }

    /// <summary>视口 1 行 — 半屏/整屏最小 1 行（max(1, ...)；底部 99）。</summary>
    [Fact]
    public void Scroll_MinimumStepIsOneLine()
    {
        var controller = EnterController(viewport: 1);
        Assert.True(controller.TryHandleKey(Parse("ctrl+u")));
        Assert.Equal(98, controller.ScrollOffset);
        Assert.True(controller.TryHandleKey(Parse("ctrl+b")));
        Assert.Equal(97, controller.ScrollOffset);
    }

    /// <summary>uniform 键重复批 — 'ggg' 幂等跳顶部（次数无关，只防泄漏 — 基准 894-898）。</summary>
    [Fact]
    public void Batch_Ggg_IdempotentTop()
    {
        var controller = EnterController();
        Assert.True(controller.TryHandleKey(new KeyPress(KeyKind.Char, "ggg")));
        Assert.Equal(0, controller.ScrollOffset);
    }

    /// <summary>uniform 键重复批 — 'jjj' 仅单次行滚（对齐基准单 action 语义）。</summary>
    [Fact]
    public void Batch_Jjj_SingleAction()
    {
        var controller = EnterController();
        Assert.True(controller.TryHandleKey(Parse("g")));
        Assert.True(controller.TryHandleKey(new KeyPress(KeyKind.Char, "jjj")));
        Assert.Equal(1, controller.ScrollOffset);
    }

    /// <summary>混合批（'gG'）— 不是键重复，不消费。</summary>
    [Fact]
    public void Batch_MixedInput_FallsThrough()
    {
        var controller = EnterController();
        Assert.False(controller.TryHandleKey(new KeyPress(KeyKind.Char, "gG")));
        Assert.Equal(90, controller.ScrollOffset);
    }

    /// <summary>未消费键 — 落到宿主继续分派（不滚动、不退出）。</summary>
    [Theory]
    [InlineData("x")]
    [InlineData("Q")]
    [InlineData("ctrl+x")]
    [InlineData("ctrl+o")]
    [InlineData("meta+j")]
    [InlineData("shift-j")]
    [InlineData("pageup")]
    [InlineData("pagedown")]
    public void Unconsumed_KeysFallThrough(string code)
    {
        var controller = EnterController();
        Assert.False(controller.TryHandleKey(Parse(code)));
        Assert.True(controller.IsActive);
        Assert.Equal(90, controller.ScrollOffset);
    }

    /// <summary>未激活 — 一切键不消费。</summary>
    [Fact]
    public void Inactive_ConsumesNothing()
    {
        var lines = Enumerable.Range(0, 100).Select(i => $"L{i:000}").ToArray();
        var controller = new TranscriptController(() => new TranscriptSnapshot(lines, 0), _ => { })
        {
            ViewportHeight = 10,
        };
        Assert.False(controller.TryHandleKey(Parse("j")));
        Assert.False(controller.TryHandleKey(Parse("/")));
        Assert.False(controller.TryHandleKey(Parse("q")));
        Assert.False(controller.TryHandleKey(KeyPress.Named(KeyKind.Escape)));
    }

    /// <summary>q — 退出 transcript（ungated）。</summary>
    [Fact]
    public void Exit_QuitKey()
    {
        var controller = EnterController();
        Assert.True(controller.TryHandleKey(Parse("q")));
        Assert.False(controller.IsActive);
    }

    /// <summary>Esc — 退出 transcript（ungated）。</summary>
    [Fact]
    public void Exit_EscapeKey()
    {
        var controller = EnterController();
        Assert.True(controller.TryHandleKey(KeyPress.Named(KeyKind.Escape)));
        Assert.False(controller.IsActive);
    }

    /// <summary>'/' — 打开搜索栏。</summary>
    [Fact]
    public void Search_SlashOpensBar()
    {
        var controller = EnterController();
        Assert.True(controller.TryHandleKey(Parse("/")));
        Assert.True(controller.Search.BarOpen);
        Assert.True(controller.IsActive);
    }

    /// <summary>搜索栏内 — 可打印字符增量入查询（含 '/' 本身成为查询字符）。</summary>
    [Fact]
    public void SearchBar_PrintableTypesIntoQuery()
    {
        var controller = EnterController();
        Assert.True(controller.TryHandleKey(Parse("/")));
        Assert.True(controller.TryHandleKey(Parse("h")));
        Assert.True(controller.TryHandleKey(Parse("i")));
        Assert.True(controller.TryHandleKey(Parse("/")));
        Assert.Equal("hi/", controller.Search.Query);
    }

    /// <summary>搜索栏内 — Backspace 删字符。</summary>
    [Fact]
    public void SearchBar_Backspace()
    {
        var controller = EnterController();
        Assert.True(controller.TryHandleKey(Parse("/")));
        Assert.True(controller.TryHandleKey(Parse("h")));
        Assert.True(controller.TryHandleKey(KeyPress.Named(KeyKind.Backspace)));
        Assert.Equal(string.Empty, controller.Search.Query);
    }

    /// <summary>搜索栏内 — Return 提交关闭栏、查询保留（n/N 继续工作）。</summary>
    [Fact]
    public void SearchBar_ReturnCommits_KeepsQuery()
    {
        var controller = EnterController();
        Assert.True(controller.TryHandleKey(Parse("/")));
        Assert.True(controller.TryHandleKey(Parse("L")));
        Assert.True(controller.TryHandleKey(KeyPress.Named(KeyKind.Return)));
        Assert.False(controller.Search.BarOpen);
        Assert.Equal("L", controller.Search.Query);
    }

    /// <summary>搜索栏内 — Esc 撤销：查询清空 + 滚动回 '/' 打开时锚点（锚 10，预览曾跳到 90）。</summary>
    [Fact]
    public void SearchBar_EscapeUndoes_ToAnchor()
    {
        var controller = EnterController();
        Assert.True(controller.TryHandleKey(Parse("g")));
        Assert.True(controller.TryHandleKey(Parse("ctrl+f")));
        Assert.Equal(10, controller.ScrollOffset);
        Assert.True(controller.TryHandleKey(Parse("/")));
        // "L09" 仅匹配 L090..L099（行 90-99）— 锚 10 之上无匹配，预览回绕跳到行 90。
        Assert.True(controller.TryHandleKey(Parse("L")));
        Assert.True(controller.TryHandleKey(new KeyPress(KeyKind.Char, "09")));
        Assert.Equal(90, controller.ScrollOffset);
        Assert.True(controller.TryHandleKey(KeyPress.Named(KeyKind.Escape)));
        Assert.Equal(string.Empty, controller.Search.Query);
        Assert.Equal(10, controller.ScrollOffset);
    }

    /// <summary>搜索栏内 — ctrl+c / ctrl+g 同 Esc 撤销。</summary>
    [Theory]
    [InlineData("ctrl+c")]
    [InlineData("ctrl+g")]
    public void SearchBar_CtrlCOrG_Undoes(string code)
    {
        var controller = EnterController();
        Assert.True(controller.TryHandleKey(Parse("/")));
        Assert.True(controller.TryHandleKey(Parse("L")));
        Assert.True(controller.TryHandleKey(Parse(code)));
        Assert.Equal(string.Empty, controller.Search.Query);
        Assert.False(controller.Search.BarOpen);
    }

    /// <summary>搜索栏内 — ctrl+o 不消费（穿透给宿主执行 toggle 退出 — 基准 ungated）。</summary>
    [Fact]
    public void SearchBar_CtrlO_FallsThrough()
    {
        var controller = EnterController();
        Assert.True(controller.TryHandleKey(Parse("/")));
        Assert.True(controller.TryHandleKey(Parse("L")));
        Assert.False(controller.TryHandleKey(Parse("ctrl+o")));
        Assert.True(controller.IsActive);
        Assert.True(controller.Search.BarOpen);
    }

    /// <summary>搜索栏内 — 方向键不消费（readline 不处理，穿透宿主）。</summary>
    [Fact]
    public void SearchBar_ArrowKeys_FallThrough()
    {
        var controller = EnterController();
        Assert.True(controller.TryHandleKey(Parse("/")));
        Assert.False(controller.TryHandleKey(KeyPress.Named(KeyKind.UpArrow)));
        Assert.False(controller.TryHandleKey(KeyPress.Named(KeyKind.DownArrow)));
        Assert.True(controller.Search.BarOpen);
    }

    /// <summary>搜索输入预览 — 就近跳转：从底部输入无后续匹配回绕首个并滚到匹配行。</summary>
    [Fact]
    public void Search_PreviewJumpsToNearestMatch()
    {
        var controller = EnterController(WithHitMarkers(100));
        Assert.Equal(90, controller.ScrollOffset);
        Assert.True(controller.TryHandleKey(Parse("/")));
        Assert.True(controller.TryHandleKey(Parse("h")));
        Assert.True(controller.TryHandleKey(Parse("i")));
        Assert.True(controller.TryHandleKey(Parse("t")));
        Assert.Equal("hit", controller.Search.Query);
        Assert.NotNull(controller.Search.Current);
        Assert.Equal(new TranscriptMatch(0, 5, 3), controller.Search.Current!);
        Assert.Equal(0, controller.ScrollOffset);
    }

    /// <summary>n — 跳下一匹配并滚动到匹配行（匹配行对齐视口顶）。</summary>
    [Fact]
    public void Search_Next_ScrollsToMatch()
    {
        var controller = EnterController(WithHitMarkers(100));
        OpenAndCommitSearch(controller, "hit");
        Assert.True(controller.TryHandleKey(Parse("n")));
        Assert.Equal(25, controller.ScrollOffset);
        Assert.Equal(2, controller.Search.CurrentOrdinal);
    }

    /// <summary>uniform 批 'nnn' — 每字符步进一次（3 步，对齐基准 for 循环）。</summary>
    [Fact]
    public void Search_NnnBatch_StepsPerChar()
    {
        var controller = EnterController(WithHitMarkers(100));
        OpenAndCommitSearch(controller, "hit");
        Assert.Equal(0, controller.Search.CurrentIndex);
        Assert.True(controller.TryHandleKey(new KeyPress(KeyKind.Char, "nnn")));
        Assert.Equal(3, controller.Search.CurrentIndex);
    }

    /// <summary>N — 跳上一匹配（越过首个回绕末个）。</summary>
    [Fact]
    public void Search_Prev_WrapsToLast()
    {
        var controller = EnterController(WithHitMarkers(100));
        OpenAndCommitSearch(controller, "hit");
        Assert.True(controller.TryHandleKey(Parse("N")));
        Assert.Equal(3, controller.Search.CurrentIndex);
        Assert.Equal(75, controller.ScrollOffset);
    }

    /// <summary>搜索栏已提交后 '/' — 重开栏预填上次查询（less shows last pattern）。</summary>
    [Fact]
    public void Search_Reopen_RefillsQuery()
    {
        var controller = EnterController(WithHitMarkers(100));
        OpenAndCommitSearch(controller, "hit");
        Assert.True(controller.TryHandleKey(Parse("/")));
        Assert.True(controller.Search.BarOpen);
        Assert.Equal("hit", controller.Search.Query);
    }

    /// <summary>无匹配时 n — 不消费（fall through 宿主）。</summary>
    [Fact]
    public void Search_NoMatch_N_FallsThrough()
    {
        var controller = EnterController(WithHitMarkers(100));
        OpenAndCommitSearch(controller, "zzz");
        Assert.False(controller.TryHandleKey(Parse("n")));
    }

    /// <summary>构造带 hit 标记的行集（每 25 行一处 — 匹配行 0/25/50/75，"L000 hit marker" 中 hit 在列 5）。</summary>
    private static string[] WithHitMarkers(int count) =>
        [.. Enumerable.Range(0, count).Select(i => i % 25 == 0 ? $"L{i:000} hit marker" : $"L{i:000}")];

    /// <summary>打开搜索栏输入查询并提交（Return — 栏关查询保留）。</summary>
    private static void OpenAndCommitSearch(TranscriptController controller, string query)
    {
        Assert.True(controller.TryHandleKey(Parse("/")));
        foreach (var ch in query)
        {
            Assert.True(controller.TryHandleKey(new KeyPress(KeyKind.Char, ch.ToString())));
        }

        Assert.True(controller.TryHandleKey(KeyPress.Named(KeyKind.Return)));
        Assert.False(controller.Search.BarOpen);
    }

    /// <summary>transcript 模态按 v 触发打开浏览委托，接收 join 后的全文文本（对齐基准 REPL.tsx:4340-4382 less-style v）。</summary>
    [Fact]
    public void V_OpensTranscriptInEditor_WithJoinedLines()
    {
        string? received = null;
        var lines = new[] { "L000", "L001", "L002" };
        var controller = new TranscriptController(() => new TranscriptSnapshot(lines, 0), _ => { }, t => received = t)
        {
            ViewportHeight = 10,
        };
        controller.Toggle();

        var consumed = controller.TryHandleKey(Parse("v"));

        Assert.True(consumed);
        Assert.Equal("L000\nL001\nL002", received);
    }

    /// <summary>未注入打开浏览委托时按 v — 静默返回 true 不崩溃（无浏览能力降级）。</summary>
    [Fact]
    public void V_WhenNoDelegate_ReturnsTrueSilently()
    {
        var controller = EnterController();
        var consumed = controller.TryHandleKey(Parse("v"));
        Assert.True(consumed);
    }

    /// <summary>搜索栏打开时按 v — v 进搜索栏文本不触发打开浏览（搜索栏优先消费裸字符）。</summary>
    [Fact]
    public void V_WhenSearchBarOpen_EntersSearchTextNotEditor()
    {
        string? received = null;
        var lines = new[] { "L000", "L001" };
        var controller = new TranscriptController(() => new TranscriptSnapshot(lines, 0), _ => { }, t => received = t)
        {
            ViewportHeight = 10,
        };
        controller.Toggle();
        controller.TryHandleKey(Parse("/"));

        controller.TryHandleKey(Parse("v"));

        Assert.Null(received);
        Assert.True(controller.Search.BarOpen);
        Assert.Contains("v", controller.Search.Query);
    }
}
