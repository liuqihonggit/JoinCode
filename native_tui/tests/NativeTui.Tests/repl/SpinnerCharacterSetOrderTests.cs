namespace NativeTui.Tests.Repl;

/// <summary>
/// spinner 字符集分支序测试（TASK069 B-5 — 对齐基准 Spinner/utils.ts:4-11 ghostty 优先于 darwin）：
/// mac+ghostty 组合须命中 Ghostty 字符集（规避 ✽ 在 ghostty 的渲染偏移 — 基准注释明言），
/// 旧 darwin 优先分支序在该组合显示基准刻意规避的 ✽。
/// </summary>
public sealed class SpinnerCharacterSetOrderTests
{
    /// <summary>mac+ghostty → Ghostty 集（✽ 被替换为 *）。</summary>
    [Fact]
    public void DarwinGhostty_UsesGhosttyCharacters()
    {
        var frames = NativeTui.Core.Repl.SpinnerCharsets.Of("xterm-ghostty", isDarwin: true);

        Assert.DoesNotContain("✽", frames);
        Assert.Contains("*", frames);
    }

    /// <summary>mac+非 ghostty → Darwin 集（既有行为回归守卫）。</summary>
    [Fact]
    public void DarwinNonGhostty_UsesDarwinCharacters()
    {
        var frames = NativeTui.Core.Repl.SpinnerCharsets.Of("xterm-256color", isDarwin: true);

        Assert.Contains("✽", frames);
    }

    /// <summary>linux+ghostty → Ghostty 集（既有行为回归守卫）。</summary>
    [Fact]
    public void LinuxGhostty_UsesGhosttyCharacters()
    {
        var frames = NativeTui.Core.Repl.SpinnerCharsets.Of("xterm-ghostty", isDarwin: false);

        Assert.DoesNotContain("✽", frames);
    }

    /// <summary>linux+非 ghostty → Default 集（既有行为回归守卫）。</summary>
    [Fact]
    public void LinuxDefault_UsesDefaultFrames()
    {
        var frames = NativeTui.Core.Repl.SpinnerCharsets.Of(null, isDarwin: false);

        Assert.Equal(NativeTui.Core.Repl.SpinnerCharsets.DefaultFrames, frames);
    }
}
