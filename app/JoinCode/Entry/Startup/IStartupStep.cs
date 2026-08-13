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
}
