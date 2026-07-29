namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// Shell 命令执行上下文接口 — 对齐 TS ShellCommand
/// 封装正在运行的进程，支持前台转后台操作
/// </summary>
public interface IShellCommandContext : IShellLifecycle
{
    /// <summary>
    /// 任务 ID — 对齐 TS taskOutput.taskId
    /// </summary>
    string TaskId { get; }

    /// <summary>
    /// 当前状态 — 对齐 TS ShellCommand.status
    /// </summary>
    ShellCommandStatus Status { get; }

    /// <summary>
    /// 执行结果 Task — 对齐 TS ShellCommand.result
    /// </summary>
    Task<ShellExecutionResult> ResultTask { get; }

    /// <summary>
    /// 原始命令
    /// </summary>
    string Command { get; }

    /// <summary>
    /// 是否允许自动后台化 — 对齐 TS shouldAutoBackground
    /// </summary>
    bool ShouldAutoBackground { get; }

    /// <summary>
    /// 将进程转为后台运行 — 对齐 TS ShellCommand.background()
    /// 不杀进程，只改变状态标记
    /// </summary>
    /// <param name="taskId">后台任务 ID</param>
    /// <returns>是否成功转后台</returns>
    bool Background(string taskId);

    /// <summary>
    /// 杀进程 — 对齐 TS ShellCommand.kill()
    /// 使用 tree-kill(SIGKILL) 强制终止整个进程树
    /// </summary>
    void Kill();

    /// <summary>
    /// 中断进程 — 对齐 TS ShellCommand.#abortHandler reason==='interrupt'
    /// 用户提交新消息时触发，不杀进程，而是将运行中的命令转为后台任务
    /// 与 Kill() 的区别：interrupt 保留进程继续运行，让模型可以看到部分输出
    /// </summary>
    /// <returns>是否成功转后台（仅 Running 状态可转）</returns>
    bool Interrupt();

    /// <summary>
    /// 启动 Assistant 自动后台化定时器 — 对齐 TS BashTool assistant auto-background
    /// 在 Assistant 模式下，命令运行超过 15s 自动转后台
    /// </summary>
    void StartAssistantAutoBackgroundTimer();

    /// <summary>
    /// 获取当前已收集的 stdout
    /// </summary>
    string GetCurrentStdout();

    /// <summary>
    /// 获取当前已收集的 stderr
    /// </summary>
    string GetCurrentStderr();

    /// <summary>
    /// 输出文件路径 — 对齐 TS TaskOutput.outputFile
    /// 后台模式下输出溢出到磁盘时的文件路径，模型可通过此路径读取命令输出
    /// </summary>
    string? OutputFilePath { get; }
}

/// <summary>
/// Shell 命令后台化常量 — 对齐 TS BashTool 常量
/// </summary>
public static class ShellBackgroundConstants
{
    /// <summary>
    /// 后台化预算 — 对齐 TS ASSISTANT_BLOCKING_BUDGET_MS (15s)
    /// Assistant 模式下，命令运行超过此时间自动转后台
    /// </summary>
    public const int AssistantBlockingBudgetMs = 15_000;

    /// <summary>
    /// 前台注册阈值 — 对齐 TS PROGRESS_THRESHOLD_MS (2s)
    /// 命令运行超过此时间才注册为前台任务
    /// </summary>
    public const int ProgressThresholdMs = 2_000;

    /// <summary>
    /// 判断命令是否允许自动后台化 — 对齐 TS isAutobackgroundingAllowed
    /// </summary>
    public static bool IsAutoBackgroundAllowed(string command)
    {
        var trimmed = command.TrimStart();
        var spaceIndex = trimmed.IndexOf(' ');
        var baseCmd = spaceIndex > 0 ? trimmed[..spaceIndex] : trimmed;
        baseCmd = Path.GetFileNameWithoutExtension(baseCmd);
        return !DisallowedAutoBackgroundCommands.Contains(baseCmd);
    }

    /// <summary>
    /// 禁止自动后台化的命令 — 对齐 TS DISALLOWED_AUTO_BACKGROUND_COMMANDS
    /// Bash: sleep; PowerShell: start-sleep, sleep
    /// </summary>
    internal static readonly FrozenSet<string> DisallowedAutoBackgroundCommands = new[] { "sleep", "start-sleep" }
        .ToFrozenSet(StringComparer.OrdinalIgnoreCase);
}
