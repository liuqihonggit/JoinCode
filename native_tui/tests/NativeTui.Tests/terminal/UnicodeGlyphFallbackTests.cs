namespace NativeTui.Tests.Terminal;

/// <summary>
/// Unicode 字形能力探测与提示符降级测试 — 对齐基准 npm figures 库 isUnicodeSupported()：
/// win32 下需 WT_SESSION / TERM_PROGRAM∈{vscode,vscode-insiders} / TERM=xterm-256color / CI 之任一，
/// 否则降级（pointer '❯' → '>'）；非 win32 仅 Linux 内核控制台（TERM=linux）降级。
/// 环境字典注入纯函数（脱离宿主终端确定性驱动）；PointerFor 锁定降级映射本身。
/// </summary>
public sealed class UnicodeGlyphFallbackTests
{
    /// <summary>构造环境字典（(键, 值) 对 → 忽略大小写查询就绪的只读字典）。</summary>
    private static IReadOnlyDictionary<string, string?> Env(params (string Key, string? Value)[] entries) =>
        entries.ToDictionary(entry => entry.Key, entry => (string?)entry.Value);

    [Fact]
    public void Unicode_WindowsLegacyConsole_NoSignals_False()
    {
        // Windows legacy console（conhost）无任何能力信号 → 降级（否则 ❯ 渲染成空心方块）
        Assert.False(TerminalCapabilities.IsUnicodeSupported(Env(), isWindows: true));
    }

    [Theory]
    [InlineData("WT_SESSION", "6c1f9e40-1e2b-4b1f-9d3a-6d7c3f0a1b2c")]
    [InlineData("TERM_PROGRAM", "vscode")]
    [InlineData("TERM_PROGRAM", "vscode-insiders")]
    [InlineData("TERM", "xterm-256color")]
    [InlineData("CI", "true")]
    public void Unicode_Windows_AnySignal_True(string key, string value)
    {
        Assert.True(TerminalCapabilities.IsUnicodeSupported(Env((key, value)), isWindows: true));
    }

    [Fact]
    public void Unicode_NonWindows_DefaultTrue()
    {
        Assert.True(TerminalCapabilities.IsUnicodeSupported(Env(), isWindows: false));
    }

    [Fact]
    public void Unicode_LinuxKernelConsole_False()
    {
        // 非 win32 唯一降级分支：TERM=linux（Linux 内核控制台无完整 Unicode 字形）
        Assert.False(TerminalCapabilities.IsUnicodeSupported(Env(("TERM", "linux")), isWindows: false));
    }

    [Fact]
    public void PointerFor_Unicode_IsReferencePointer()
    {
        Assert.Equal("❯", Figures.PointerFor(asciiFallback: false));
    }

    [Fact]
    public void PointerFor_AsciiFallback_MatchesFiguresLibrary()
    {
        // 基准 figures 库 fallback 表：pointer → '>'
        Assert.Equal(">", Figures.PointerFor(asciiFallback: true));
    }
}
