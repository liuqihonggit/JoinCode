namespace NativeTui.Tests.Repl;

/// <summary>
/// HyperlinkEmitter 测试 — Wrap 序列形态（ST 终止，委托 Osc.Hyperlink/HyperlinkEnd 既有常量）/
/// mailto 降级（基准 markdown.ts:143-147 — 显示 href 提取的 email、忽略锚文本）/空白 URL 纯文本/
/// 中文载荷形态/null 参数防御；IsHyperlinkSupported 探测分支（TERM_PROGRAM 与 LC_TERMINAL 白名单/
/// WT_SESSION/kitty/未知保守 false/键大小写不敏感/tmux 不排除）。环境字典注入纯函数确定性驱动
/// （对齐 TerminalCapabilitiesTests 同模式，脱离宿主终端）。
/// </summary>
public sealed class HyperlinkEmitterTests
{
    /// <summary>构造环境字典（(键, 值) 对 → 忽略大小写查询就绪的只读字典）。</summary>
    private static IReadOnlyDictionary<string, string?> Env(params (string Key, string? Value)[] entries) =>
        entries.ToDictionary(entry => entry.Key, entry => (string?)entry.Value);

    [Fact]
    public void Wrap_PlainUrl_EmitsOsc8WrappedSequence()
    {
        // 形态精确锁定：ESC ]8;;url ST text ESC ]8;; ST（ST = ESC \，基准 createHyperlink 同构）
        Assert.Equal(
            "\x1b]8;;https://github.com/anthropics/claude-code\x1b\\release notes\x1b]8;;\x1b\\",
            HyperlinkEmitter.Wrap("release notes", "https://github.com/anthropics/claude-code"));
    }

    [Fact]
    public void Wrap_ChineseText_PreservedInsideSequence()
    {
        // 中文锚文本与 URL 载荷原样透传（OSC 8 payload 不转义 — 基准同拼接语义）
        Assert.Equal(
            "\x1b]8;;https://example.com/页面\x1b\\中文链接\x1b]8;;\x1b\\",
            HyperlinkEmitter.Wrap("中文链接", "https://example.com/页面"));
    }

    [Fact]
    public void Wrap_MailtoUrl_DegradesToPlainEmail_IgnoringText()
    {
        // 基准 markdown.ts:143-147 — mailto 链接降级显示 href 提取的 email（锚文本丢弃）
        Assert.Equal("team@example.com", HyperlinkEmitter.Wrap("联系我们", "mailto:team@example.com"));
    }

    [Fact]
    public void Wrap_MailtoPrefix_IsCaseSensitiveLikeBaseline()
    {
        // 基准 startsWith('mailto:') 区分大小写 — 大写前缀不走降级（Ordinal 锁定）
        Assert.Equal(
            "\x1b]8;;MAILTO:a@b.com\x1b\\t\x1b]8;;\x1b\\",
            HyperlinkEmitter.Wrap("t", "MAILTO:a@b.com"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Wrap_NullOrBlankUrl_ReturnsPlainText(string? url)
    {
        // 空/空白 URL 无链接意义 — 纯文本返回
        Assert.Equal("纯文本", HyperlinkEmitter.Wrap("纯文本", url));
    }

    [Fact]
    public void Wrap_NullText_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => HyperlinkEmitter.Wrap(null!, "https://example.com"));
    }

    [Theory]
    [InlineData("ghostty")]
    [InlineData("Hyper")]
    [InlineData("kitty")]
    [InlineData("alacritty")]
    [InlineData("iTerm.app")]
    [InlineData("iTerm2")]
    [InlineData("WezTerm")]
    [InlineData("vscode")]
    [InlineData("WarpTerminal")]
    [InlineData("contour")]
    public void Support_TermProgramWhitelist_True(string termProgram)
    {
        // 基准 ADDITIONAL_HYPERLINK_TERMINALS + 库常见项合并白名单
        Assert.True(HyperlinkEmitter.IsHyperlinkSupported(Env(("TERM_PROGRAM", termProgram))));
    }

    [Fact]
    public void Support_TermProgramNotListed_False()
    {
        // 未知 TERM_PROGRAM 保守 false（不发未声明序列）
        Assert.False(HyperlinkEmitter.IsHyperlinkSupported(
            Env(("TERM_PROGRAM", "Apple_Terminal"), ("TERM", "xterm-256color"))));
    }

    [Fact]
    public void Support_LcTerminal_True()
    {
        // LC_TERMINAL 在 tmux 内保留 — TERM_PROGRAM 被覆盖时的外层终端兜底（基准分支同译）
        Assert.True(HyperlinkEmitter.IsHyperlinkSupported(Env(("LC_TERMINAL", "iTerm.app"))));
    }

    [Fact]
    public void Support_WindowsTerminal_True()
    {
        Assert.True(HyperlinkEmitter.IsHyperlinkSupported(Env(("WT_SESSION", "uuid"))));
    }

    [Theory]
    [InlineData("xterm-kitty")]
    [InlineData("xterm-kitty-256color")]
    public void Support_TermContainsKitty_True(string term)
    {
        Assert.True(HyperlinkEmitter.IsHyperlinkSupported(Env(("TERM", term))));
    }

    [Fact]
    public void Support_KittyWindowIdSet_True()
    {
        // kitty 可能只设 KITTY_WINDOW_ID 不设 TERM
        Assert.True(HyperlinkEmitter.IsHyperlinkSupported(Env(("KITTY_WINDOW_ID", "42"))));
    }

    [Fact]
    public void Support_EmptyEnvironment_False()
    {
        Assert.False(HyperlinkEmitter.IsHyperlinkSupported(Env()));
    }

    [Fact]
    public void Support_EmptyStringValue_TreatedAsUnset()
    {
        // 空串视为未设置（对齐基准 truthy 判定）
        Assert.False(HyperlinkEmitter.IsHyperlinkSupported(
            Env(("WT_SESSION", ""), ("TERM_PROGRAM", ""), ("TERM", ""))));
    }

    [Fact]
    public void Support_EnvKeys_CaseInsensitive()
    {
        // Windows 环境块不区分大小写 — 归一化后小写键等同大写键
        Assert.True(HyperlinkEmitter.IsHyperlinkSupported(Env(("wt_session", "uuid"))));
    }

    [Fact]
    public void Support_TmuxSession_NotExcluded()
    {
        // OSC 8 逐字节透传无损 — tmux 不排除（与 DEC 2026 同步输出的 tmux 排除策略语义不同）；
        // tmux 内 LC_TERMINAL 保留外层终端探测
        Assert.True(HyperlinkEmitter.IsHyperlinkSupported(
            Env(("TMUX", "/tmp/tmux-0/default,1234,0"), ("LC_TERMINAL", "iTerm2"))));
    }
}
