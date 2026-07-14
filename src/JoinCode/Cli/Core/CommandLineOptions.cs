namespace JoinCode;

public class CommandLineOptions {
    /// <summary>
    /// 显示帮助信息（--help / -h）
    /// </summary>
    public bool ShowHelp { get; set; }

    /// <summary>
    /// 显示版本信息（--version / -v）
    /// </summary>
    public bool ShowVersion { get; set; }

    /// <summary>
    /// 管道名称，用于命名管道通信模式
    /// </summary>
    public string? PipeName { get; set; }

    /// <summary>
    /// 是否使用管道模式
    /// </summary>
    public bool IsPipeMode => !string.IsNullOrWhiteSpace(PipeName);

    /// <summary>
    /// 非交互模式标志（--non-interactive 参数）
    /// </summary>
    public bool NonInteractive { get; set; }

    /// <summary>
    /// 自动信任工作目录（--trust 参数），跳过信任目录确认弹窗。
    /// 用于 E2E 测试和 CI/CD 环境。
    /// </summary>
    public bool TrustWorkspace { get; set; }

    /// <summary>
    /// 直接传入的提示词（-p 或位置参数）
    /// </summary>
    public string? Prompt { get; set; }

    /// <summary>
    /// 指定模型 ID（--model 参数，优先级高于环境变量和 settings.json）
    /// </summary>
    public string? Model { get; set; }

    /// <summary>
    /// 检测到的无头模式原因（由 ParseArgs 设置）
    /// </summary>
    public HeadlessMode DetectedHeadlessMode { get; set; } = HeadlessMode.Interactive;

    /// <summary>
    /// 启动时激活简要模式 — 对齐 TS --brief CLI 标志 / maybeActivateBrief()
    /// </summary>
    public bool Brief { get; set; }

    /// <summary>
    /// 强制交互模式（--force-interactive 参数）— 即使 stdin 重定向也启用 REPL，用于 E2E 测试
    /// </summary>
    public bool ForceInteractive { get; set; }

    /// <summary>
    /// 超时自动关闭秒数（--await N 参数）— 超时后进程强制退出并返回 1234
    /// 用于测试诊断卡死问题，正常完成不受影响
    /// </summary>
    public int? AwaitTimeoutSeconds { get; set; }

    /// <summary>
    /// 启用诊断输出（--verbose 参数）— 等效于 JCC_VERBOSE=1 环境变量
    /// 激活 [WIRE] [STEP] [READY] [MAIN] 等诊断日志输出到 stderr
    /// </summary>
    public bool Verbose { get; set; }

    /// <summary>
    /// 继续最近的会话（--continue / -c 参数）— 自动选择最近一次会话恢复
    /// 对齐 TS: claude --continue（自动选择 last conversation）
    /// </summary>
    public bool ContinueSession { get; set; }

    /// <summary>
    /// 恢复指定会话（--resume &lt;session-id&gt; / -r &lt;session-id&gt; 参数）
    /// 支持完整 sessionId 或自定义标题关键字模糊匹配
    /// 对齐 TS: claude --resume &lt;session-id&gt;
    /// </summary>
    public string? ResumeSessionId { get; set; }

    /// <summary>
    /// 权限模式字符串（--permission-mode &lt;mode&gt; 参数）
    /// 值为 default/plan/auto/ask/deny/acceptEdits/bypassPermissions
    /// 在 ParseArgs 中映射到 JCC_PERMISSION_MODE 环境变量（供 PermissionChecker 读取）
    /// 对齐 TS: claude --permission-mode &lt;mode&gt;
    /// </summary>
    public string? PermissionMode { get; set; }

    /// <summary>
    /// 跳过所有权限检查（--dangerously-skip-permissions 参数）
    /// 等价于 --permission-mode bypassPermissions 的快捷方式
    /// 在 ParseArgs 中映射到 JCC_PERMISSION_MODE=bypassPermissions 环境变量
    /// 对齐 TS: claude --dangerously-skip-permissions
    /// </summary>
    public bool DangerouslySkipPermissions { get; set; }

    /// <summary>
    /// 是否为非交互模式（用户请求 / 无头环境 / CI 环境 / -p 参数）
    /// </summary>
    public bool IsNonInteractiveMode =>
        !ForceInteractive
        && (NonInteractive
            || !string.IsNullOrWhiteSpace(Prompt)
            || DetectedHeadlessMode != HeadlessMode.Interactive);
}
