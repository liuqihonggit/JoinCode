using JoinCode.Abstractions.Transport;
using JoinCode.Abstractions.Configuration.Llm;

namespace JoinCode.Abstractions.Configuration;

public class WorkflowConfig {
    /// <summary>
    /// Provider 配置（推荐）
    /// </summary>
    public ProviderConfig Provider { get; set; } = new ProviderConfig();

    /// <summary>
    /// 获取当前模型ID（从Provider配置）
    /// </summary>
    public string CurrentModelId => Provider.ModelId;

    public string StateFilePath { get; set; } = WorkflowConstants.Paths.DefaultStateFilePath;

    public string? MemdirPath { get; set; }

    public string SkillsDirectory { get; set; } = string.Empty;

    public CodeExecutionConfig CodeExecution { get; set; } = new CodeExecutionConfig();

    public BridgeConfig Bridge { get; set; } = new BridgeConfig();

    /// <summary>
    /// 是否启用跨进程文件锁
    /// </summary>
    public bool EnableCrossProcessLock { get; set; } = true;

    /// <summary>
    /// 是否启用 Worktree 智能体隔离
    /// </summary>
    public bool EnableWorktreeIsolation { get; set; } = true;

    /// <summary>
    /// Worktree 配置
    /// </summary>
    public WorktreeConfig Worktree { get; set; } = new WorktreeConfig();

    /// <summary>
    /// 文件操作配置
    /// </summary>
    public FileOperationConfig FileOperation { get; set; } = new FileOperationConfig();

    /// <summary>
    /// Shell 执行配置
    /// </summary>
    public ShellExecutionConfig ShellExecution { get; set; } = new ShellExecutionConfig();

    /// <summary>
    /// 项目规则内容（从 .jcc/rules/project_rules.md 加载）
    /// </summary>
    public string? ProjectRules { get; set; }

    /// <summary>
    /// 外部规则文件列表（从 .trae/rules/ + .claude/rules/ + .codex/rules/ 加载，含匹配策略）
    /// </summary>
    public List<RuleFile> ExternalRules { get; set; } = [];

    /// <summary>
    /// 管道端点配置（用于命名管道通信模式）
    /// </summary>
    public PipeTransportConfig? PipeEndpoint { get; set; }

    /// <summary>
    /// 插件配置
    /// </summary>
    public PluginConfig Plugins { get; set; } = new PluginConfig();

    /// <summary>
    /// 空闲工具检测配置
    /// </summary>
    public IdleDetectionConfig IdleDetection { get; set; } = new IdleDetectionConfig();

    /// <summary>
    /// 快速模式（使用更小/更快的模型）
    /// </summary>
    public bool FastMode { get; set; } = false;

    /// <summary>
    /// LLM 执行参数配置（Temperature, MaxTokens, TopP 等）
    /// </summary>
    public LlmExecutionSettings LlmExecution { get; set; } = new();

    /// <summary>
    /// 缓存配置
    /// </summary>
    public CacheSettings Cache { get; set; } = new();

    /// <summary>
    /// 工具执行配置
    /// </summary>
    public ToolExecutionSettings ToolExecution { get; set; } = new();
}
