namespace NativeTui.Tests.Bridge;

/// <summary>TASK069 #13 流式预览门控策略 — 对齐基准 REPL.tsx:1495 + terminal.ts:177：
/// shouldStream = !reducedMotion && !(isWindows || WT_SESSION)。注入式纯函数（不读真实环境，可测）。</summary>
public sealed class StreamingPreviewPolicyTests
{
    [Theory]
    [InlineData(false, false, null, true)]   // 非 Windows 无 WT_SESSION → 流式
    [InlineData(true, false, null, false)]   // reducedMotion → 禁
    [InlineData(false, true, null, false)]   // Windows → 禁（conhost yank bug）
    [InlineData(false, false, "abc", false)] // WT_SESSION（WSL in Windows Terminal）→ 禁
    [InlineData(false, false, "", true)]     // WT_SESSION 空串 → 视为未设置
    [InlineData(true, true, "abc", false)]   // 全禁
    public void ShouldAllowStreaming_MatchesBaseline(bool reducedMotion, bool isWindows, string? wtSession, bool expected) =>
        Assert.Equal(expected, StreamingPreviewPolicy.ShouldAllowStreaming(reducedMotion, isWindows, wtSession));
}
