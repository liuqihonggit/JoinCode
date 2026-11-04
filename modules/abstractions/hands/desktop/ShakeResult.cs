namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 窗口震动结果 — 包含震动目标的句柄、标题、来源描述。
/// </summary>
/// <param name="Title">窗口标题。</param>
/// <param name="Handle">窗口句柄的十六进制字符串。</param>
/// <param name="Source">窗口来源（控制台窗口/父进程链/启动时前台窗口/前台窗口回退）。</param>
public sealed record ShakeResult(string Title, string Handle, string Source) {
    public override string ToString() => $"标题=\"{Title}\" 句柄={Handle} 来源={Source}";
}