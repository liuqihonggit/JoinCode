namespace JoinCode.Hands.Desktop;

/// <summary>
/// 桌面环境检测结果 — 描述当前环境是否支持交互式桌面操作
/// </summary>
public sealed record DesktopEnvironmentCheckResult(bool IsInteractive, string[] Reasons)
{
    /// <summary>交互式桌面环境</summary>
    public static readonly DesktopEnvironmentCheckResult Interactive = new(true, []);

    /// <summary>构造非交互式结果</summary>
    public static DesktopEnvironmentCheckResult NonInteractive(params string[] reasons) => new(false, reasons);

    /// <summary>环境诊断描述</summary>
    public string Diagnostic => IsInteractive
        ? "交互式桌面环境"
        : $"非交互式桌面环境: {string.Join("; ", Reasons)}";
}

/// <summary>
/// 桌面环境守卫 — 检测当前是否在交互式桌面会话中
/// CI/无头服务环境中 Win32 P/Invoke 会静默失败或行为不可靠，需提前检测并明确报错
/// </summary>
public static class DesktopEnvironmentGuard
{
    /// <summary>
    /// 检测当前环境是否支持交互式桌面操作
    /// 检测项：CI 环境变量、UserInteractive 标志、前台窗口有效性
    /// </summary>
    public static DesktopEnvironmentCheckResult CheckInteractiveDesktop()
    {
        var reasons = new List<string>(4);

        var githubActions = Environment.GetEnvironmentVariable("GITHUB_ACTIONS");
        if (githubActions == "true")
            reasons.Add("GITHUB_ACTIONS=true (GitHub Actions CI 环境)");

        var ci = Environment.GetEnvironmentVariable("CI");
        if (ci == "true")
            reasons.Add("CI=true (CI 环境)");

        if (!Environment.UserInteractive)
            reasons.Add("Environment.UserInteractive=false (非交互式会话)");

        var foreground = User32NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero)
            reasons.Add("GetForegroundWindow()=IntPtr.Zero (无前台窗口/无桌面会话)");

        return reasons.Count == 0
            ? DesktopEnvironmentCheckResult.Interactive
            : DesktopEnvironmentCheckResult.NonInteractive([.. reasons]);
    }
}
