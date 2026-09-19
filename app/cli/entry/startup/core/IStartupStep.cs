namespace JoinCode.Entry;

/// <summary>
/// 启动上下文 — 跨中间件共享数据
/// </summary>
public sealed class StartupContext {
    /// <summary>
    /// 工作流配置 — 启动各步骤共享的配置根
    /// </summary>
    public required WorkflowConfig Config { get; init; }

    /// <summary>
    /// 命令行解析选项 — 启动各步骤读取的 CLI 参数
    /// </summary>
    public required CommandLineOptions Options { get; init; }

    /// <summary>
    /// 主机实例 — 提供 DI 容器与服务解析
    /// </summary>
    public required IHost Host { get; init; }

    /// <summary>
    /// 文件系统抽象 — 启动各步骤用于读写文件
    /// </summary>
    public required IFileSystem FileSystem { get; init; }

    /// <summary>
    /// 获取或设置是否已具备 API Key
    /// </summary>
    public bool HasApiKey { get; set; }

    /// <summary>
    /// 获取或设置 CLI 会话实例；未初始化时为 null
    /// </summary>
    public CliSession? Session { get; set; }

    /// <summary>
    /// 引擎会话 ID — 由 SessionInitStep 从 IChatContextManager 读取并回填，
    /// 后续步骤（SessionResumeStep 等）复用；null 表示尚未初始化
    /// </summary>
    public string? SessionId { get; set; }

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
}