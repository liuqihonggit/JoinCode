namespace JoinCode.Abstractions.Configuration;

public sealed record WorkflowConfig {
    /// <summary>
    /// Provider 配置（推荐）
    /// </summary>
    public ProviderConfig Provider { get; init; } = new ProviderConfig();

    /// <summary>
    /// 获取当前模型ID（从Provider配置）
    /// </summary>
    public string CurrentModelId => Provider.ModelId;

    /// <summary>获取或设置状态文件路径。</summary>
    public string StateFilePath { get; init; } = WorkflowConstants.Paths.DefaultStateFilePath;

    /// <summary>获取或设置记忆目录路径。</summary>
    public string? MemdirPath { get; init; }

    /// <summary>获取或设置技能目录。</summary>
    public string SkillsDirectory { get; init; } = string.Empty;

    /// <summary>获取或设置代码执行配置。</summary>
    public CodeExecutionConfig CodeExecution { get; init; } = new CodeExecutionConfig();

    /// <summary>获取或设置桥接配置。</summary>
    public BridgeConfig Bridge { get; init; } = new BridgeConfig();

    /// <summary>
    /// 是否启用跨进程文件锁
    /// </summary>
    public bool EnableCrossProcessLock { get; init; } = true;

    /// <summary>
    /// 是否启用 Worktree 智能体隔离
    /// </summary>
    public bool EnableWorktreeIsolation { get; init; } = true;

    /// <summary>
    /// Worktree 配置
    /// </summary>
    public WorktreeConfig Worktree { get; init; } = new WorktreeConfig();

    /// <summary>
    /// 文件操作配置
    /// </summary>
    public FileOperationConfig FileOperation { get; init; } = new FileOperationConfig();

    /// <summary>
    /// Shell 执行配置
    /// </summary>
    public ShellExecutionConfig ShellExecution { get; init; } = new ShellExecutionConfig();

    /// <summary>
    /// 项目规则内容（从 .jcc/rules/project_rules.md 加载）
    /// </summary>
    public string? ProjectRules { get; init; }

    /// <summary>
    /// 外部规则文件列表（从 .trae/rules/ + .claude/rules/ + .codex/rules/ 加载，含匹配策略）
    /// </summary>
    public List<RuleFile> ExternalRules { get; init; } = [];

    /// <summary>
    /// 管道端点配置（用于命名管道通信模式）
    /// </summary>
    public PipeTransportConfig? PipeEndpoint { get; init; }

    /// <summary>
    /// 插件配置
    /// </summary>
    public PluginConfig Plugins { get; init; } = new PluginConfig();

    /// <summary>
    /// 空闲工具检测配置
    /// </summary>
    public IdleDetectionConfig IdleDetection { get; init; } = new IdleDetectionConfig();

    /// <summary>
    /// 快速模式（使用更小/更快的模型）
    /// </summary>
    public bool FastMode { get; init; } = false;

    /// <summary>
    /// 语言偏好 — 对齐 TS settings.json language 键（如 "chinese"/"japanese"），
    /// 流向 SystemPromptProviderOptions.LanguagePreference（LanguageSection 生成语言偏好提示词）
    /// </summary>
    public string? Language { get; init; }

    /// <summary>
    /// 输出风格名 — 对齐 TS settings.json outputStyle 键：内建 explanatory/learning + 自定义
    /// （~/.jcc/output-styles/ + {cwd}/.jcc/output-styles/ 双层目录，项目覆盖用户）；
    /// 流向 SystemPromptProviderOptions.OutputStyle（OutputStyleSection 注入风格 prompt）
    /// </summary>
    public string? OutputStyle { get; init; }

    /// <summary>
    /// 自动记忆开关 — 对齐 TS settings.json autoMemoryEnabled 键（默认 true）；
    /// 流向 Dream AutoDreamConfig.AutoMemoryEnabled（插件桥）
    /// </summary>
    public bool AutoMemoryEnabled { get; init; } = true;

    /// <summary>
    /// 自动做梦（后台记忆整理）开关 — 对齐 TS settings.json autoDreamEnabled 键（默认 true）；
    /// 流向 Dream AutoDreamConfig.Enabled（插件桥）
    /// </summary>
    public bool AutoDreamEnabled { get; init; } = true;

    /// <summary>
    /// 显示回合耗时 — 对齐 TS showTurnDuration（默认 true）；
    /// SessionController 门控 timing 摘要显示（debuglog 开启时强制显示）
    /// </summary>
    public bool ShowTurnDuration { get; init; } = true;

    /// <summary>
    /// 自动压缩开关 — 对齐 TS autoCompactEnabled（默认 true）；
    /// AutoCompactService.ShouldAutoCompact 门控（false 时即使超硬阈值也不触发）
    /// </summary>
    public bool AutoCompactEnabled { get; init; } = true;

    /// <summary>
    /// 文件检查点开关 — 对齐 TS fileCheckpointingEnabled（默认 true）；
    /// FileHistoryService.BackupBeforeWriteAsync 门控（false 时跳过写前备份）
    /// </summary>
    public bool FileCheckpointingEnabled { get; init; } = true;

    /// <summary>
    /// LLM 执行参数配置（Temperature, MaxTokens, TopP 等）
    /// </summary>
    public LlmExecutionSettings LlmExecution { get; init; } = new();

    /// <summary>
    /// 缓存配置
    /// </summary>
    public CacheSettings Cache { get; init; } = new();

    /// <summary>
    /// 工具执行配置
    /// </summary>
    public ToolExecutionSettings ToolExecution { get; init; } = new();

    /// <summary>
    /// 子智能体输出防护配置 — L0-L3 炸窗防护
    /// </summary>
    public SubAgentConfig SubAgent { get; init; } = new();

    /// <summary>
    /// 子代理并发控制配置 — spawn/execute/fork 三阶段上限（ADR 0048）
    /// </summary>
    public SubAgentConcurrencyOptions SubAgentConcurrency { get; init; } = new();

    /// <summary>
    /// 子代理卡死防护配置 — 纵深防御四层参数（ADR 0106）
    /// 从 settings.json 的 current.subAgentLiveness 节点加载,缺失时用默认值
    /// </summary>
    public SubAgentLivenessOptions SubAgentLiveness { get; init; } = new();

    /// <summary>
    /// Actor 模型统一配置 — 编译队列模式 + 背压预设(ADR 0074)
    /// 从 settings.json 的 current.actor 节点加载,缺失时用默认值
    /// </summary>
    public ActorSettings Actor { get; init; } = new();

    /// <summary>
    /// 当前激活的配置档案名 — 对应 settings.json 中 profiles 字典的键
    /// </summary>
    public string? CurrentProfile { get; init; }
}