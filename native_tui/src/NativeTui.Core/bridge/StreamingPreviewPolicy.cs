namespace NativeTui.Core.Bridge;

/// <summary>
/// 流式预览门控策略 — 对齐基准 screens/REPL.tsx:1495 `showStreamingText = !reducedMotion &&
/// !hasCursorUpViewportYankBug()` 与 ink/terminal.ts:177 `yank bug = win32 || WT_SESSION`：
/// 用户偏好减少动态，或终端是 Windows conhost（含 WSL-in-Windows-Terminal 的 WT_SESSION），
/// 均禁用逐字流式（避免 conhost 收到越过可见区的 cursor-up 时把视口 yank 到 scrollback 顶部）。
/// 纯函数（isWindows/wtSession 注入 — 宿主从 OperatingSystem/Environment 取值，Core 不读环境）。
/// </summary>
public static class StreamingPreviewPolicy
{
    /// <summary>是否允许逐字流式预览（true = 流式，false = 等整条消息落定）。</summary>
    public static bool ShouldAllowStreaming(bool reducedMotion, bool isWindows, string? wtSession) =>
        !reducedMotion && !isWindows && string.IsNullOrEmpty(wtSession);
}
