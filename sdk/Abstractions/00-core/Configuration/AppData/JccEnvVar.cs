namespace JoinCode.Abstractions.Configuration.AppData;

/// <summary>
/// JCC 专属环境变量名枚举 — 替代原 AppDataConstants.JccEnvVars 静态常量类
/// </summary>
public enum JccEnvVar
{
    [EnumValue("JCC_PROVIDER")] Provider,
    [EnumValue("JCC_MODEL_ID")] ModelId,
    [EnumValue("JCC_ENDPOINT")] Endpoint,
    [EnumValue("JCC_API_KEY")] ApiKey,
    [EnumValue("JCC_API_VERSION")] ApiVersion,
    [EnumValue("JCC_ORGANIZATION_ID")] OrganizationId,
    [EnumValue("JCC_ENABLE_OAUTH")] EnableOAuth,
    [EnumValue("JCC_CODE_EXECUTION_TIMEOUT")] CodeExecutionTimeout,
    [EnumValue("JCC_CODE_EXECUTION_MAX_MEMORY")] CodeExecutionMaxMemory,
    [EnumValue("JCC_FORCE_TERMINAL")] ForceTerminal,
    [EnumValue("JCC_EFFORT_LEVEL")] EffortLevel,
    [EnumValue("JCC_ASSISTANT_MODE")] AssistantMode,
    [EnumValue("JCC_APP_DATA_FOLDER")] AppDataFolder,
    [EnumValue("JCC_AUTH_FILE_NAME")] AuthFileName,
    [EnumValue("JCC_SETTINGS_FILE_NAME")] SettingsFileName,
    [EnumValue("JCC_GLOBAL_CONFIG_FILE_NAME")] GlobalConfigFileName,
    [EnumValue("JCC_RULES_FOLDER_NAME")] RulesFolderName,
    [EnumValue("JCC_PROJECT_RULES_FILE_NAME")] ProjectRulesFileName,
    [EnumValue("JCC_SCHEDULED_TASKS_FILE_NAME")] ScheduledTasksFileName,
    [EnumValue("JCC_CUSTOM_MODEL_OPTION")] CustomModelOption,
    [EnumValue("JCC_CUSTOM_MODEL_OPTION_NAME")] CustomModelOptionName,
    [EnumValue("JCC_CUSTOM_MODEL_OPTION_DESCRIPTION")] CustomModelOptionDescription,
    [EnumValue("JCC_SKILL_CHAR_BUDGET")] SkillCharBudget,
    [EnumValue("JCC_SKIP_WEB_FETCH_PREFLIGHT")] SkipWebFetchPreflight,

    // 新增: 运行时配置
    [EnumValue("JCC_SANDBOX_MODE")] SandboxMode,
    [EnumValue("JCC_LANGUAGE")] Language,
    [EnumValue("JCC_STATUS_LINE_COMMAND")] StatusLineCommand,
    [EnumValue("JCC_REPL_MODE")] ReplMode,
    [EnumValue("JCC_REMOTE_MEMORY_DIR")] RemoteMemoryDir,
    [EnumValue("JCCSHARPTUI_AZURE_CLIENT_ID")] AzureClientId,

    // 新增: AppData 路径覆盖
    [EnumValue("JCC_CREDENTIALS_FILE_NAME")] CredentialsFileName,
    [EnumValue("JCC_TEAMS_FOLDER_NAME")] TeamsFolderName,
    [EnumValue("JCC_TASKS_FOLDER_NAME")] TasksFolderName,
    [EnumValue("JCC_WORKTREES_FOLDER_NAME")] WorktreesFolderName,
    [EnumValue("JCC_AGENTS_FOLDER_NAME")] AgentsFolderName,
    [EnumValue("JCC_THEME_FILE_NAME")] ThemeFileName,
    [EnumValue("JCC_TRUSTED_FOLDERS_FILE_NAME")] TrustedFoldersFileName,
    [EnumValue("JCC_SESSIONS_FOLDER_NAME")] SessionsFolderName,
    [EnumValue("JCC_SESSION_META_FILE_NAME")] SessionMetaFileName,
    [EnumValue("JCC_COMMANDS_FOLDER_NAME")] CommandsFolderName,
    [EnumValue("JCC_MAILBOX_FOLDER_NAME")] MailboxFolderName,
    [EnumValue("JCC_FILE_HISTORY_FOLDER_NAME")] FileHistoryFolderName,
    [EnumValue("JCC_PLANS_FOLDER_NAME")] PlansFolderName,
    [EnumValue("JCC_TOOL_RESULTS_FOLDER_NAME")] ToolResultsFolderName,

    // 新增: Team/Teammate 环境变量
    [EnumValue("JCC_AUTO_MODE_GATE_ENABLED")] AutoModeGateEnabled,
    [EnumValue("JCC_TEAM_ID")] TeamId,
    [EnumValue("JCC_TEAM_NAME")] TeamName,
    [EnumValue("JCC_TEAMMATE_ID")] TeammateId,
    [EnumValue("JCC_TEAMMATE_NAME")] TeammateName,
    [EnumValue("JCC_TEAMMATE_ROLE")] TeammateRole,
    [EnumValue("JCC_TEAMMATE_COLOR")] TeammateColor,
    [EnumValue("JCC_TEAMMATE_IN_PROCESS")] TeammateInProcess,
    [EnumValue("JCC_COORDINATOR_ID")] CoordinatorId,
    [EnumValue("JCC_LEAD_AGENT_ID")] LeadAgentId,
    [EnumValue("JCC_TEAM_ALLOWED_PATHS")] TeamAllowedPaths,
    [EnumValue("JCC_PLAN_MODE_REQUIRED")] PlanModeRequired,
    [EnumValue("JCC_PARENT_SESSION_ID")] ParentSessionId,

    // 新增: Brief 模式环境变量绕过（对齐 TS CLAUDE_CODE_BRIEF）
    [EnumValue("JCC_BRIEF")] Brief,

    [EnumValue("JCC_STATE_FILE_PATH")] StateFilePath,

    // 新增: Bridge 环境变量（对齐 TS 端 CLAUDE_CODE_*）
    [EnumValue("JCC_SESSION_ACCESS_TOKEN")] SessionAccessToken,
    [EnumValue("JCC_BRIDGE_USE_CCR_V2")] BridgeUseCcrV2,

    // 新增: 启动时权限模式覆盖 — 支持 E2E 测试自动升级权限（如 bypassPermissions）
    [EnumValue("JCC_PERMISSION_MODE")] PermissionMode,

    /// <summary>
    /// 文件系统后端模式 — Physical（默认，真实磁盘）/ InMemory（纯内存，0磁盘IO，调试/E2E测试用）
    /// </summary>
    [EnumValue("JCC_FILE_SYSTEM_MODE")] FileSystemMode,

    /// <summary>
    /// HTTP 客户端模式 — Real（默认，真实网络）/ Mock（拦截请求返回预设响应，调试/E2E测试用）
    /// </summary>
    [EnumValue("JCC_HTTP_MODE")] HttpMode,

    /// <summary>
    /// 遥测总开关 — 设为 false 同时关闭 tracing 和 metrics（无需分别设两个变量）
    /// </summary>
    [EnumValue("JCC_TELEMETRY_ENABLED")] TelemetryEnabled,

    /// <summary>
    /// 通知模式 — Windows（默认，气泡通知）/ Console（纯日志输出，调试用）
    /// </summary>
    [EnumValue("JCC_NOTIFICATION_MODE")] NotificationMode,

    /// <summary>
    /// 浏览器自动化模式 — None（默认，NoOp）/ Puppeteer（启用浏览器自动化）
    /// </summary>
    [EnumValue("JCC_BROWSER_AUTOMATION")] BrowserAutomation,

    /// <summary>
    /// 任务服务模式 — File（默认，文件持久化）/ Memory（纯内存，调试/E2E测试用）
    /// </summary>
    [EnumValue("JCC_TASK_SERVICE_MODE")] TaskServiceMode,

    /// <summary>
    /// 状态服务模式 — File（默认，SQLite持久化）/ InMemory（纯内存0磁盘IO，调试/E2E测试用）
    /// </summary>
    [EnumValue("JCC_STATE_MODE")] StateMode,

    /// <summary>
    /// 进程服务模式 — Physical（默认，真实进程）/ NoOp（跳过所有进程操作，调试/E2E测试用）
    /// </summary>
    [EnumValue("JCC_PROCESS_MODE")] ProcessMode,

    /// <summary>
    /// 时钟模式 — Physical（默认，真实系统时间）/ Fake（可控时间，调试/E2E测试用）
    /// </summary>
    [EnumValue("JCC_CLOCK_MODE")] ClockMode,

    /// <summary>
    /// 控制台输出模式 — Physical（默认，真实控制台）/ NoOp（静默所有输出，E2E测试/CI用）
    /// </summary>
    [EnumValue("JCC_CONSOLE_MODE")] ConsoleMode,

    // API 配置
    [EnumValue("JCC_API_BASE_URL")] ApiBaseUrl,
    [EnumValue("JCC_API_MAX_INPUT_TOKENS")] ApiMaxInputTokens,
    [EnumValue("JCC_API_TARGET_INPUT_TOKENS")] ApiTargetInputTokens,
    [EnumValue("JCC_MAX_CONTEXT_TOKENS")] MaxContextTokens,

    // Bridge 配置
    [EnumValue("JCC_BRIDGE_MODE")] BridgeMode,
    [EnumValue("JCC_BRIDGE_CONNECT_TIMEOUT_MS")] BridgeConnectTimeoutMs,
    [EnumValue("JCC_BRIDGE_HEARTBEAT_INTERVAL")] BridgeHeartbeatInterval,
    [EnumValue("JCC_BRIDGE_HEARTBEAT_MS")] BridgeHeartbeatMs,
    [EnumValue("JCC_BRIDGE_HTTP_TIMEOUT_MS")] BridgeHttpTimeoutMs,
    [EnumValue("JCC_BRIDGE_INIT_RETRY_BASE_MS")] BridgeInitRetryBaseMs,
    [EnumValue("JCC_BRIDGE_INIT_RETRY_MAX")] BridgeInitRetryMax,
    [EnumValue("JCC_BRIDGE_INIT_RETRY_MAX_MS")] BridgeInitRetryMaxMs,
    [EnumValue("JCC_BRIDGE_MIN_VERSION")] BridgeMinVersion,
    [EnumValue("JCC_BRIDGE_MS_POLL_AT_CAPACITY")] BridgeMsPollAtCapacity,
    [EnumValue("JCC_BRIDGE_MS_POLL_NOT_AT_CAPACITY")] BridgeMsPollNotAtCapacity,
    [EnumValue("JCC_BRIDGE_MS_POLL_PARTIAL_CAPACITY")] BridgeMsPollPartialCapacity,
    [EnumValue("JCC_BRIDGE_POLL_INTERVAL_AT_CAPACITY")] BridgePollIntervalAtCapacity,
    [EnumValue("JCC_BRIDGE_POLL_INTERVAL_NOT_AT_CAPACITY")] BridgePollIntervalNotAtCapacity,
    [EnumValue("JCC_BRIDGE_RECLAIM_OLDER_THAN_MS")] BridgeReclaimOlderThanMs,
    [EnumValue("JCC_BRIDGE_TEARDOWN_MS")] BridgeTeardownMs,
    [EnumValue("JCC_BRIDGE_TOKEN_REFRESH_MS")] BridgeTokenRefreshMs,

    // 调试/诊断
    [EnumValue("JCC_VERBOSE")] Verbose,
    [EnumValue("JCC_DEBUG_MODULES")] DebugModules,
    [EnumValue("JCC_DI_TRACE")] DiTrace,
    [EnumValue("JCC_DUMP_MESSAGES")] DumpMessages,
    [EnumValue("JCC_LOG_LEVEL")] LogLevel,

    // 遥测细分
    [EnumValue("JCC_TELEMETRY_EXPORT")] TelemetryExport,
    [EnumValue("JCC_TELEMETRY_METRICS")] TelemetryMetrics,
    [EnumValue("JCC_TELEMETRY_TRACING")] TelemetryTracing,

    // 运行时/会话
    [EnumValue("JCC_ENVIRONMENT_KIND")] EnvironmentKind,
    [EnumValue("JCC_SESSION_KIND")] SessionKind,
    [EnumValue("JCC_ENTRYPOINT")] Entrypoint,
    [EnumValue("JCC_EXEC_PATH")] ExecPath,
    [EnumValue("JCC_USE_API_CLEAR_TOOL_RESULTS")] UseApiClearToolResults,
    [EnumValue("JCC_USE_API_CLEAR_TOOL_USES")] UseApiClearToolUses,
}

/// <summary>
/// Provider 专属环境变量名枚举
/// </summary>
public enum ProviderEnvVar
{
    [EnumValue("OPENAI_API_KEY")] OpenAiApiKey,
    [EnumValue("AZURE_OPENAI_API_KEY")] AzureOpenAiApiKey,
    [EnumValue("AZURE_OPENAI_ENDPOINT")] AzureOpenAiEndpoint,
    [EnumValue("ANTHROPIC_API_KEY")] AnthropicApiKey,
    [EnumValue("AGNES_API_KEY")] AgnesApiKey,
    [EnumValue("DEEPSEEK_API_KEY")] DeepSeekApiKey,
}
