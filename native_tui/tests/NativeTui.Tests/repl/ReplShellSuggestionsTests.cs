namespace NativeTui.Tests.Repl;

/// <summary>
/// ReplShell 斜杠补全集成测试 — 对齐基准 useTypeahead.tsx 键位仲裁（Autocomplete 上下文优先于 Chat）：
/// 输入 / 触发建议；建议激活时 Up/Down 循环导航、Tab 接受不执行（补 "/cmd "）、Enter 接受并提交、
/// Esc 仅关建议（优先于打断/清输入/退出）；编辑键后刷新；历史填充不触发建议；无命令清单安全降级。
/// </summary>
public sealed class ReplShellSuggestionsTests
{
    /// <summary>测试命令集。</summary>
    private static readonly IReadOnlyList<CommandSuggestion> Commands =
    [
        new("help", "显示帮助", [], IsHidden: false),
        new("hello", "问候", [], IsHidden: false),
        new("compact", "压缩上下文", [], IsHidden: false),
        new("theme", "切换主题", [], IsHidden: false, Arguments: ["dark", "light", "auto"]),
    ];

    /// <summary>构造带命令清单的测试 shell（SeedContent 就绪消息区）。</summary>
    private static (ReplShell Shell, FakeReplEngine Engine) CreateShell(IReadOnlyList<CommandSuggestion>? commands = null)
    {
        var engine = new FakeReplEngine();
        var banner = new BannerItem("1.0.0", "test-model", "API 按量计费", "/tmp");
        var shell = new ReplShell(engine, banner, new StringWriter(), commands: commands ?? Commands);
        shell.SeedContent();
        return (shell, engine);
    }

    /// <summary>按键注入（功能键）。</summary>
    private static void Press(ReplShell shell, ConsoleKey key) =>
        shell.HandleKey(new ConsoleKeyInfo('\0', key, false, false, false));

    /// <summary>键入文本（经 HandleKey 全链路 — 触发建议刷新）。</summary>
    private static void Type(ReplShell shell, string text)
    {
        foreach (var c in text)
        {
            shell.HandleKey(new ConsoleKeyInfo(c, ConsoleKey.None, false, false, false));
        }
    }

    /// <summary>输入 / 后建议激活且首项预选。</summary>
    [Fact]
    public void SlashInput_ActivatesSuggestions()
    {
        var (shell, _) = CreateShell();
        Type(shell, "/");
        Assert.True(shell.Suggester?.IsActive);
        Assert.Equal(4, shell.Suggester?.Items.Count);
        Assert.Equal("compact", shell.Suggester?.Selected?.Name);
    }

    /// <summary>前缀输入过滤：/he 命中 help/hello。</summary>
    [Fact]
    public void PrefixInput_FiltersSuggestions()
    {
        var (shell, _) = CreateShell();
        Type(shell, "/he");
        Assert.Equal(["help", "hello", "theme"], shell.Suggester!.Items.Select(i => i.Name).ToArray());
    }

    /// <summary>普通文本输入不触发建议。</summary>
    [Fact]
    public void PlainInput_NoSuggestions()
    {
        var (shell, _) = CreateShell();
        Type(shell, "hello world");
        Assert.False(shell.Suggester?.IsActive);
    }

    /// <summary>退格后建议刷新：/c 仅 compact；退格回 / 后全量 4 条。</summary>
    [Fact]
    public void Backspace_RefreshesSuggestions()
    {
        var (shell, _) = CreateShell();
        Type(shell, "/c");
        Assert.Equal(["compact"], shell.Suggester!.Items.Select(i => i.Name).ToArray());
        Press(shell, ConsoleKey.Backspace);
        Assert.Equal(4, shell.Suggester!.Items.Count);
    }

    /// <summary>建议激活时 Up/Down 循环导航（不消费历史）。</summary>
    [Fact]
    public void SuggestionsActive_ArrowKeys_NavigateSuggestions()
    {
        var (shell, _) = CreateShell();
        Type(shell, "/he");
        Press(shell, ConsoleKey.DownArrow);
        Assert.Equal(1, shell.Suggester?.SelectedIndex);
        Press(shell, ConsoleKey.UpArrow);
        Assert.Equal(0, shell.Suggester?.SelectedIndex);
        Press(shell, ConsoleKey.UpArrow);
        Assert.Equal(2, shell.Suggester?.SelectedIndex);
    }

    /// <summary>Tab 接受：补全为 "/help "（带尾随空格），不提交引擎，建议关闭。</summary>
    [Fact]
    public void Tab_AcceptsSuggestionWithoutSubmit()
    {
        var (shell, engine) = CreateShell();
        Type(shell, "/he");
        Press(shell, ConsoleKey.Tab);
        Assert.Equal("/help ", shell.Input.Text);
        Assert.Equal("/help ".Length, shell.Input.CursorCol);
        Assert.False(shell.Suggester?.IsActive);
        Assert.Empty(engine.Submits);
    }

    /// <summary>Enter 接受并执行：提交 "/help" 给引擎。</summary>
    [Fact]
    public void Enter_AcceptsSuggestionAndSubmits()
    {
        var (shell, engine) = CreateShell();
        Type(shell, "/he");
        Press(shell, ConsoleKey.Enter);
        Assert.Equal(["/help"], engine.Submits);
        Assert.Equal(string.Empty, shell.Input.Text);
    }

    /// <summary>Esc 仅关建议：输入保留，不退出 REPL。</summary>
    [Fact]
    public void Escape_DismissesSuggestionsKeepsInput()
    {
        var (shell, _) = CreateShell();
        Type(shell, "/he");
        Press(shell, ConsoleKey.Escape);
        Assert.Equal("/he", shell.Input.Text);
        Assert.False(shell.Suggester?.IsActive);
        Assert.True(shell.Running);
    }

    /// <summary>Esc 抑制同一输入：移动键不重触发（文本不变不刷新）；文本变化后重触发。</summary>
    [Fact]
    public void Escape_SuppressesSameInput()
    {
        var (shell, _) = CreateShell();
        Type(shell, "/hel");
        Press(shell, ConsoleKey.Escape);
        Assert.False(shell.Suggester?.IsActive);
        Press(shell, ConsoleKey.RightArrow);
        Assert.False(shell.Suggester?.IsActive);
        Type(shell, "p");
        Assert.True(shell.Suggester?.IsActive);
    }

    /// <summary>空命令清单（安全降级）：无补全能力，输入 / 不崩溃。</summary>
    [Fact]
    public void NoCommandCatalog_SafeDegradation()
    {
        var (shell, _) = CreateShell(commands: []);
        Type(shell, "/he");
        Assert.Null(shell.Suggester);
        Assert.True(shell.Running);
    }

    /// <summary>历史导航填充不触发建议（翻到 / 开头历史条目时建议保持关闭）。</summary>
    [Fact]
    public void HistoryNavigation_DoesNotTriggerSuggestions()
    {
        var (shell, _) = CreateShell();
        shell.SeedHistory(["/help"]);
        Press(shell, ConsoleKey.UpArrow);
        Assert.Equal("/help", shell.Input.Text);
        Assert.False(shell.Suggester?.IsActive);
    }

    /// <summary>参数补全（TASK057-2）：命令后空格弹参数建议，Tab 接受为 "/命令 参数 "。</summary>
    [Fact]
    public void ArgumentCompletion_Tab_AcceptsParameterValue()
    {
        var (shell, _) = CreateShell();
        Type(shell, "/theme ");
        Assert.True(shell.Suggester?.IsArgumentMode);
        Assert.Equal(["dark", "light", "auto"], shell.Suggester!.Items.Select(i => i.Name).ToArray());
        Press(shell, ConsoleKey.Tab);
        Assert.Equal("/theme dark ", shell.Input.Text);
        Assert.False(shell.Suggester?.IsActive);
    }

    /// <summary>参数前缀过滤 + Tab 接受：/theme l → light。</summary>
    [Fact]
    public void ArgumentCompletion_PrefixFilter_AcceptsFiltered()
    {
        var (shell, _) = CreateShell();
        Type(shell, "/theme l");
        Assert.Equal(["light"], shell.Suggester!.Items.Select(i => i.Name).ToArray());
        Press(shell, ConsoleKey.Tab);
        Assert.Equal("/theme light ", shell.Input.Text);
    }

    /// <summary>参数模式 Enter：接受并提交 — S11 语义等价迁移：/theme 已被 ReplShell 本地拦截
    ///（S9 接线：Enter 经参数建议接受为 "/theme dark" 后本地切换渲染色板不透传引擎），
    /// 断言更新为拦截语义（不透传 + 输入清空 + 用户命令上屏）；
    /// 通用"参数 Enter 接受并提交"语义由 ReplShellWiringCommandUxTests 用非拦截命令载体覆盖。</summary>
    [Fact]
    public void ArgumentCompletion_Enter_AcceptsAndSubmits()
    {
        var (shell, engine) = CreateShell();
        Type(shell, "/theme d");
        Press(shell, ConsoleKey.Enter);
        Assert.Equal([], engine.Submits);
        Assert.Equal(string.Empty, shell.Input.Text);
        Assert.Contains(shell.View.Messages.Items, m => m is UserMessageItem { Text: "/theme dark" });
    }

    /// <summary>命令接受后进入参数态：Tab 接受命令 → 参数建议自动弹出。</summary>
    [Fact]
    public void CommandAccept_TabThenArguments_Chain()
    {
        var (shell, _) = CreateShell();
        Type(shell, "/the");
        Press(shell, ConsoleKey.Tab);
        Assert.Equal("/theme ", shell.Input.Text);
        Assert.True(shell.Suggester?.IsArgumentMode);
        Assert.Equal(["dark", "light", "auto"], shell.Suggester!.Items.Select(i => i.Name).ToArray());
    }

    /// <summary>幽灵文本（TASK057-3）：中途 "/comp" token 计算出 ghost，不弹下拉。</summary>
    [Fact]
    public void GhostText_MidInputSlash_ComputedWithoutPopup()
    {
        var (shell, _) = CreateShell();
        Type(shell, "run /comp");
        Assert.Equal("act", shell.Suggester?.GhostSuffix);
        Assert.False(shell.Suggester?.IsActive);
    }

    /// <summary>幽灵文本右箭头接受：光标在末尾 + ghost 激活 → RightArrow 补全文本。</summary>
    [Fact]
    public void GhostText_RightArrow_AtEnd_AcceptsGhost()
    {
        var (shell, _) = CreateShell();
        Type(shell, "run /comp");
        Press(shell, ConsoleKey.RightArrow);
        Assert.Equal("run /compact", shell.Input.Text);
        Assert.Equal("run /compact".Length, shell.Input.CursorCol);
    }

    /// <summary>光标不在末尾：RightArrow 保持正常光标移动（不接受 ghost）。</summary>
    [Fact]
    public void GhostText_RightArrow_MidText_MovesCursorOnly()
    {
        var (shell, _) = CreateShell();
        Type(shell, "run /comp");
        Press(shell, ConsoleKey.LeftArrow);
        Press(shell, ConsoleKey.RightArrow);
        Assert.Equal("run /comp", shell.Input.Text);
    }

    /// <summary>无 ghost：RightArrow 在末尾为 no-op（文本不变）。</summary>
    [Fact]
    public void GhostText_Absent_RightArrowAtEnd_NoOp()
    {
        var (shell, _) = CreateShell();
        Type(shell, "plain");
        Press(shell, ConsoleKey.RightArrow);
        Assert.Equal("plain", shell.Input.Text);
    }

    /// <summary>Esc 关闭 ghost：同输入抑制（与下拉共用 dismiss）。</summary>
    [Fact]
    public void GhostText_Escape_DismissesAndSuppresses()
    {
        var (shell, _) = CreateShell();
        Type(shell, "run /comp");
        Assert.Equal("act", shell.Suggester?.GhostSuffix);
        Press(shell, ConsoleKey.Escape);
        Assert.Equal(string.Empty, shell.Suggester?.GhostSuffix);
        Assert.Equal("run /comp", shell.Input.Text);
        Assert.True(shell.Running);
        Press(shell, ConsoleKey.LeftArrow);
        Press(shell, ConsoleKey.RightArrow);
        Assert.Equal("run /comp", shell.Input.Text);
    }
}
