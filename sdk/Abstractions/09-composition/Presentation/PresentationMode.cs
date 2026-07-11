namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 表示层模式 — 决定使用 CLI 还是 TUI
/// </summary>
public enum PresentationMode
{
    /// <summary>
    /// 命令行模式 — 纯文本输出，适合自动化测试和管道
    /// </summary>
    Cli,

    /// <summary>
    /// 终端用户界面模式 — 完整交互式 TUI
    /// </summary>
    Tui,

    /// <summary>
    /// 无头模式 — 无输出，适合后台任务
    /// </summary>
    Headless
}
