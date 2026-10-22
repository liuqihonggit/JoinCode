namespace NativeTui.Tests.Terminal;

/// <summary>
/// 终端能力探测测试 — 判定分支逐项对齐基准 ink/terminal.ts isSynchronizedOutputSupported：
/// tmux 排除 / TERM_PROGRAM 白名单 / kitty / xterm-ghostty / foot 前缀 / alacritty 包含 /
/// ZED_TERM / WT_SESSION / VTE_VERSION 阈值；未知终端保守 false。
/// 环境字典注入纯函数（脱离宿主终端确定性驱动）；DetectSpinnerFrames 委托契约一并锁定。
/// </summary>
public sealed class TerminalCapabilitiesTests
{
    /// <summary>构造环境字典（(键, 值) 对 → 忽略大小写查询就绪的只读字典）。</summary>
    private static IReadOnlyDictionary<string, string?> Env(params (string Key, string? Value)[] entries) =>
        entries.ToDictionary(entry => entry.Key, entry => (string?)entry.Value);

    [Fact]
    public void Sync_EmptyEnvironment_ConservativeFalse()
    {
        // 未知终端保守 false（不发未声明的 DEC 2026 序列）
        Assert.False(TerminalCapabilities.IsSynchronizedOutputSupported(Env()));
    }

    [Fact]
    public void Sync_TmuxSession_ExcludedEvenWithSupportSignals()
    {
        // tmux 透传分块破坏原子性 — 即便同时带 WT_SESSION/kitty 信号也排除（判定序第一）
        Assert.False(TerminalCapabilities.IsSynchronizedOutputSupported(
            Env(("TMUX", "/tmp/tmux-0/default,1234,0"), ("WT_SESSION", "uuid"), ("TERM", "xterm-kitty"))));
    }

    [Theory]
    [InlineData("iTerm.app")]
    [InlineData("WezTerm")]
    [InlineData("WarpTerminal")]
    [InlineData("ghostty")]
    [InlineData("contour")]
    [InlineData("vscode")]
    [InlineData("alacritty")]
    public void Sync_TermProgramWhitelist_True(string termProgram)
    {
        Assert.True(TerminalCapabilities.IsSynchronizedOutputSupported(
            Env(("TERM_PROGRAM", termProgram))));
    }

    [Fact]
    public void Sync_TermProgramNotListed_False()
    {
        Assert.False(TerminalCapabilities.IsSynchronizedOutputSupported(
            Env(("TERM_PROGRAM", "Apple_Terminal"), ("TERM", "xterm-256color"))));
    }

    [Theory]
    [InlineData("xterm-kitty")]
    [InlineData("xterm-kitty-256color")]
    public void Sync_TermContainsKitty_True(string term)
    {
        Assert.True(TerminalCapabilities.IsSynchronizedOutputSupported(Env(("TERM", term))));
    }

    [Fact]
    public void Sync_KittyWindowIdSet_True()
    {
        // kitty 可能只设 KITTY_WINDOW_ID 不设 TERM
        Assert.True(TerminalCapabilities.IsSynchronizedOutputSupported(
            Env(("KITTY_WINDOW_ID", "42"))));
    }

    [Fact]
    public void Sync_TermXtermGhostty_True()
    {
        // ghostty 可能只设 TERM=xterm-ghostty 而无 TERM_PROGRAM
        Assert.True(TerminalCapabilities.IsSynchronizedOutputSupported(Env(("TERM", "xterm-ghostty"))));
    }

    [Theory]
    [InlineData("foot")]
    [InlineData("foot-extra")]
    public void Sync_TermStartsWithFoot_True(string term)
    {
        Assert.True(TerminalCapabilities.IsSynchronizedOutputSupported(Env(("TERM", term))));
    }

    [Theory]
    [InlineData("alacritty")]
    [InlineData("xterm-alacritty-256color")]
    public void Sync_TermContainsAlacritty_True(string term)
    {
        Assert.True(TerminalCapabilities.IsSynchronizedOutputSupported(Env(("TERM", term))));
    }

    [Fact]
    public void Sync_ZedTerm_True()
    {
        // Zed 内嵌 alacritty_terminal crate（支持 DEC 2026）
        Assert.True(TerminalCapabilities.IsSynchronizedOutputSupported(Env(("ZED_TERM", "1"))));
    }

    [Fact]
    public void Sync_WindowsTerminal_True()
    {
        Assert.True(TerminalCapabilities.IsSynchronizedOutputSupported(Env(("WT_SESSION", "uuid"))));
    }

    [Theory]
    [InlineData("6800")]
    [InlineData("7600")]
    [InlineData("13600")]
    public void Sync_VteVersionAtLeast6800_True(string vteVersion)
    {
        // VTE 0.68 起支持（版本号 ×100 编码）
        Assert.True(TerminalCapabilities.IsSynchronizedOutputSupported(Env(("VTE_VERSION", vteVersion))));
    }

    [Theory]
    [InlineData("6700")]
    [InlineData("6799")]
    [InlineData("not-a-number")]
    public void Sync_VteVersionBelowOrInvalid_False(string vteVersion)
    {
        Assert.False(TerminalCapabilities.IsSynchronizedOutputSupported(Env(("VTE_VERSION", vteVersion))));
    }

    [Fact]
    public void Sync_EnvKeys_CaseInsensitive()
    {
        // Windows 环境块不区分大小写 — 归一化后 wt_session 小写键等同 WT_SESSION
        Assert.True(TerminalCapabilities.IsSynchronizedOutputSupported(
            Env(("wt_session", "uuid"))));
    }

    [Fact]
    public void Sync_EmptyStringValue_TreatedAsUnset()
    {
        // 空串视为未设置（对齐基准 truthy 判定）— WT_SESSION="" 不触发支持
        Assert.False(TerminalCapabilities.IsSynchronizedOutputSupported(
            Env(("WT_SESSION", ""), ("VTE_VERSION", ""))));
    }

    [Fact]
    public void Sync_NullDictionaryValue_TreatedAsUnset()
    {
        Assert.False(TerminalCapabilities.IsSynchronizedOutputSupported(
            Env(("TERM_PROGRAM", (string?)null))));
    }

    [Fact]
    public void DetectSpinnerFrames_MatchesSpinnerCharsetsDetection()
    {
        // 统一能力入口委托契约：同一探测源（Detect 进程内缓存一次 — 两次调用等值）
        Assert.Equal(SpinnerCharsets.Detect(), TerminalCapabilities.DetectSpinnerFrames());
    }

    [Fact]
    public void DetectSpinnerFrames_TwelveFrameBounceSequence()
    {
        // 12 帧往返弹跳序列形态锁定（6 字符集 + 反转拼接）
        var frames = TerminalCapabilities.DetectSpinnerFrames();
        Assert.Equal(12, frames.Length);
        Assert.Equal(frames[..6], frames[6..].Reverse().ToArray());
    }
}
