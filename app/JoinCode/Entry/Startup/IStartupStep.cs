namespace JoinCode.Entry;

/// <summary>
/// 启动上下文 — 跨中间件共享数据
/// </summary>
public sealed class StartupContext
{
    public required WorkflowConfig Config { get; init; }
    public required CommandLineOptions Options { get; init; }
    public required IHost Host { get; init; }
    public required IFileSystem FileSystem { get; init; }
    public bool HasApiKey { get; set; }
    public CliSession? Session { get; set; }

    /// <summary>
    /// 非交互模式的提示词 — 由 PromptStep 设置，由 ExecuteStep 消费
    /// </summary>
    public string? NonInteractivePrompt { get; set; }

    /// <summary>
    /// 退出码 — 非交互模式由中间件设置，0 表示成功
    /// </summary>
    public int ExitCode { get; set; }

    /// <summary>
    /// CLI 输出契约 — JSON 模式下由 NonInteractiveModeRunner 创建，中间件可用来写结构化输出
    /// </summary>
    public Cli.Output.CliOutputContract? OutputContract { get; set; }

    /// <summary>
    /// 非交互模式的完整响应文本 — 由 ExecuteStep 设置
    /// </summary>
    public string? FullResponse { get; set; }

    /// <summary>
    /// 执行耗时（毫秒）— 由 ExecuteStep 设置
    /// </summary>
    public long? ElapsedMs { get; set; }

    /// <summary>
    /// 用户在启动时选择要 dump 的调试信息类别 — 由 DebugDumpPromptStep 设置，由 InitDebugDumpStep 消费
    /// 决策: 位标志枚举而非 bool，支持用户选择组合（如 Init+Prompt）
    /// 决策: 询问放在 WorkspaceTrustStep 之后（用户要求），dump 放在 SystemPromptApplyStep 之后（确保 system prompt 已应用）
    /// </summary>
    public DebugDumpSection DebugDumpChoice { get; set; } = DebugDumpSection.None;

    /// <summary>
    /// TUI 模式下保存的原始 Console IO — 由 InteractiveModeRunner 在重定向前保存，由 ReplLoopStep 在启动 Terminal.Gui 前恢复。
    /// 决策: 管道步骤(ProviderSetupStep/DebugDumpPromptStep)用 TerminalHelper.ReadLine 阻塞等待 Console 输入，与 Terminal.Gui 接管 Console 冲突。
    ///       TUI 模式下重定向 Console.In 为空 StringReader 让 ReadLine 立即返回空串跳过交互，Console.Out/Error 重定向为 Null 吞掉管道步骤的 Console 输出。
    ///       ReplLoopStep 的 TUI 分支恢复原始 IO 后再启动 Terminal.Gui，让 Application.Init 接管真实 Console。
    /// </summary>
    public TextReader? OriginalConsoleIn { get; set; }
    public TextWriter? OriginalConsoleOut { get; set; }
    public TextWriter? OriginalConsoleError { get; set; }
}
