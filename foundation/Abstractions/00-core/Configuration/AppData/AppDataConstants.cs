namespace JoinCode.Abstractions.Configuration.AppData;

/// <summary>
/// 应用数据目录常量 - 集中管理所有路径约定
/// 所有字段均为 static 可配置属性，支持运行时修改和环境变量覆盖
/// 环境变量命名规则: JCC_{PASCAL_CASE_NAME}，如 JCC_APP_DATA_FOLDER
/// 所有环境变量名通过 JccEnvVar 枚举引用，禁止硬编码
///
/// 注意: 此类已标记为遗留，新代码应使用 AppDataPaths 不可变记录类
/// set 访问器仅用于测试隔离，生产代码不应修改
/// </summary>
public static class AppDataConstants
{
    private static AppDataPaths _paths = AppDataPaths.Default;

    /// <summary>
    /// 当前路径配置（不可变记录类实例）
    /// 测试中替换此实例替代修改单个属性
    /// </summary>
    public static AppDataPaths Paths
    {
        get => _paths;
        set => _paths = value;
    }

    /// <summary>
    /// 应用数据目录名（位于用户主目录下）
    /// </summary>
    public static string AppDataFolder
    {
        get => _paths.AppDataFolder;
        set => _paths = _paths with { AppDataFolder = value };
    }

    /// <summary>
    /// OAuth 凭证文件名
    /// </summary>
    public static string CredentialsFileName
    {
        get => _paths.CredentialsFileName;
        set => _paths = _paths with { CredentialsFileName = value };
    }

    /// <summary>
    /// 认证文件名
    /// </summary>
    public static string AuthFileName
    {
        get => _paths.AuthFileName;
        set => _paths = _paths with { AuthFileName = value };
    }

    /// <summary>
    /// 设置文件名
    /// </summary>
    public static string SettingsFileName
    {
        get => _paths.SettingsFileName;
        set => _paths = _paths with { SettingsFileName = value };
    }

    /// <summary>
    /// 全局配置文件名
    /// </summary>
    public static string GlobalConfigFileName
    {
        get => _paths.GlobalConfigFileName;
        set => _paths = _paths with { GlobalConfigFileName = value };
    }

    /// <summary>
    /// 规则目录名（位于 AppDataFolder 下）
    /// </summary>
    public static string RulesFolderName
    {
        get => _paths.RulesFolderName;
        set => _paths = _paths with { RulesFolderName = value };
    }

    /// <summary>
    /// 项目规则文件名
    /// </summary>
    public static string ProjectRulesFileName
    {
        get => _paths.ProjectRulesFileName;
        set => _paths = _paths with { ProjectRulesFileName = value };
    }

    /// <summary>
    /// 调度任务文件名
    /// </summary>
    public static string ScheduledTasksFileName
    {
        get => _paths.ScheduledTasksFileName;
        set => _paths = _paths with { ScheduledTasksFileName = value };
    }

    /// <summary>
    /// 团队目录名
    /// </summary>
    public static string TeamsFolderName
    {
        get => _paths.TeamsFolderName;
        set => _paths = _paths with { TeamsFolderName = value };
    }

    /// <summary>
    /// 任务目录名
    /// </summary>
    public static string TasksFolderName
    {
        get => _paths.TasksFolderName;
        set => _paths = _paths with { TasksFolderName = value };
    }

    /// <summary>
    /// Worktree 目录名
    /// </summary>
    public static string WorktreesFolderName
    {
        get => _paths.WorktreesFolderName;
        set => _paths = _paths with { WorktreesFolderName = value };
    }

    /// <summary>
    /// Agents 目录名
    /// </summary>
    public static string AgentsFolderName
    {
        get => _paths.AgentsFolderName;
        set => _paths = _paths with { AgentsFolderName = value };
    }

    /// <summary>
    /// 主题配置文件名
    /// </summary>
    public static string ThemeFileName
    {
        get => _paths.ThemeFileName;
        set => _paths = _paths with { ThemeFileName = value };
    }

    /// <summary>
    /// 信任目录记录文件名
    /// </summary>
    public static string TrustedFoldersFileName
    {
        get => _paths.TrustedFoldersFileName;
        set => _paths = _paths with { TrustedFoldersFileName = value };
    }

    /// <summary>
    /// 会话目录名（位于 AppDataFolder 下）
    /// </summary>
    public static string SessionsFolderName
    {
        get => _paths.SessionsFolderName;
        set => _paths = _paths with { SessionsFolderName = value };
    }

    /// <summary>
    /// 会话元数据文件名
    /// </summary>
    public static string SessionMetaFileName
    {
        get => _paths.SessionMetaFileName;
        set => _paths = _paths with { SessionMetaFileName = value };
    }

    /// <summary>
    /// 自定义命令目录名（位于 AppDataFolder 下）
    /// </summary>
    public static string CommandsFolderName
    {
        get => _paths.CommandsFolderName;
        set => _paths = _paths with { CommandsFolderName = value };
    }

    /// <summary>
    /// 邮箱目录名（位于 AppDataFolder 下，用于跨进程消息持久化）
    /// </summary>
    public static string MailboxFolderName
    {
        get => _paths.MailboxFolderName;
        set => _paths = _paths with { MailboxFolderName = value };
    }

    /// <summary>
    /// 文件历史备份目录名（位于 AppDataFolder 下，用于写入前备份）
    /// </summary>
    public static string FileHistoryFolderName
    {
        get => _paths.FileHistoryFolderName;
        set => _paths = _paths with { FileHistoryFolderName = value };
    }

    /// <summary>
    /// 计划文件目录名（位于 AppDataFolder 下，用于 Plan 模式持久化）
    /// </summary>
    public static string PlansFolderName
    {
        get => _paths.PlansFolderName;
        set => _paths = _paths with { PlansFolderName = value };
    }

    /// <summary>
    /// 工具结果目录名（位于会话目录下，用于二进制内容持久化）
    /// 对齐TS版 mcpOutputStorage.ts 的 getToolResultsDir
    /// </summary>
    public static string ToolResultsFolderName
    {
        get => _paths.ToolResultsFolderName;
        set => _paths = _paths with { ToolResultsFolderName = value };
    }

    /// <summary>
    /// MCP 连接配置目录名（位于 AppDataFolder 下，用于跨进程共享 MCP 连接状态）
    /// </summary>
    public static string McpFolderName
    {
        get => _paths.McpFolderName;
        set => _paths = _paths with { McpFolderName = value };
    }

    /// <summary>
    /// MCP 连接配置文件名
    /// </summary>
    public static string McpConnectionsFileName
    {
        get => _paths.McpConnectionsFileName;
        set => _paths = _paths with { McpConnectionsFileName = value };
    }

    /// <summary>
    /// MCP 认证配置文件名
    /// </summary>
    public static string McpAuthFileName
    {
        get => _paths.McpAuthFileName;
        set => _paths = _paths with { McpAuthFileName = value };
    }

    // === 完整路径属性（委托到 AppDataPaths 计算属性） ===

    /// <summary>用户级 .jcc 根目录: ~/.jcc/</summary>
    public static string JccDirectory => _paths.JccDirectory;

    /// <summary>用户级成本跟踪目录: ~/.jcc/costs/</summary>
    public static string CostsDirectory => _paths.CostsDirectory;

    /// <summary>用户级会话历史目录: ~/.jcc/sessions/</summary>
    public static string SessionsDirectory => _paths.SessionsDirectory;

    /// <summary>用户级文件编辑历史目录: ~/.jcc/file-history/</summary>
    public static string FileHistoryDirectory => _paths.FileHistoryDirectory;

    /// <summary>用户级大文本粘贴缓存目录: ~/.jcc/paste-cache/</summary>
    public static string PasteCacheDirectory => _paths.PasteCacheDirectory;

    /// <summary>用户级 Shell 快照目录: ~/.jcc/shell-snapshots/</summary>
    public static string ShellSnapshotsDirectory => _paths.ShellSnapshotsDirectory;

    /// <summary>用户级计划文件目录: ~/.jcc/plans/</summary>
    public static string PlansDirectory => _paths.PlansDirectory;

    /// <summary>用户级任务存储目录: ~/.jcc/tasks/</summary>
    public static string TasksDirectory => _paths.TasksDirectory;

    /// <summary>用户级团队配置目录: ~/.jcc/teams/</summary>
    public static string TeamsDirectory => _paths.TeamsDirectory;

    /// <summary>用户级 MCP 配置目录: ~/.jcc/mcp/</summary>
    public static string McpDirectory => _paths.McpDirectory;

    /// <summary>用户级工具模板目录: ~/.jcc/tool-templates/</summary>
    public static string ToolTemplatesDirectory => _paths.ToolTemplatesDirectory;

    /// <summary>用户级 Agent 状态目录: ~/.jcc/agents/</summary>
    public static string AgentsDirectory => _paths.AgentsDirectory;

    /// <summary>用户级源代码克隆目录: ~/.jcc/source/</summary>
    public static string SourceDirectory => _paths.SourceDirectory;

    /// <summary>用户级关键词配置文件: ~/.jcc/keyword-sections.json</summary>
    public static string KeywordSectionsFilePath => _paths.KeywordSectionsFilePath;

    /// <summary>用户级 LSP 服务器配置文件: ~/.jcc/lsp-servers.json</summary>
    public static string LspServersFilePath => _paths.LspServersFilePath;

    /// <summary>用户级 GUI 偏好文件: ~/.jcc/gui-preferences.json</summary>
    public static string GuiPreferencesFilePath => _paths.GuiPreferencesFilePath;

    /// <summary>用户级引导完成标记文件: ~/.jcc/onboarding_complete.json</summary>
    public static string OnboardingCompleteFilePath => _paths.OnboardingCompleteFilePath;

    /// <summary>用户级运行时目录: ~/.jcc/runtime/ — 持久性运行日志、崩溃快照等（ADR 0100）</summary>
    public static string UserRuntimeDirectory => _paths.UserRuntimeDirectory;

    /// <summary>用户级运行时错误日志: ~/.jcc/runtime/jcc_error.log（ADR 0100）</summary>
    public static string UserRuntimeErrorLogPath => _paths.UserRuntimeErrorLogPath;

    /// <summary>用户级运行时 --await 超时日志: ~/.jcc/runtime/jcc_await_timeout.log（ADR 0100）</summary>
    public static string UserRuntimeAwaitTimeoutLogPath => _paths.UserRuntimeAwaitTimeoutLogPath;

    /// <summary>用户级运行时崩溃快照目录: ~/.jcc/runtime/crash-dumps/（ADR 0100）</summary>
    public static string UserRuntimeCrashDumpsDirectory => _paths.UserRuntimeCrashDumpsDirectory;

    /// <summary>用户级运行时工具结果溢出目录: ~/.jcc/runtime/tool-results/（ADR 0100）</summary>
    public static string UserRuntimeToolResultsDirectory => _paths.UserRuntimeToolResultsDirectory;

    /// <summary>用户级运行时 TUI 诊断目录: ~/.jcc/runtime/jcctui_diag/（ADR 0100）</summary>
    public static string UserRuntimeJccTuiDiagDirectory => _paths.UserRuntimeJccTuiDiagDirectory;

    /// <summary>用户级运行时剪贴板回退目录: ~/.jcc/runtime/clipboard/（ADR 0100）</summary>
    public static string UserRuntimeClipboardDirectory => _paths.UserRuntimeClipboardDirectory;

    /// <summary>用户级运行时宏文件目录: ~/.jcc/runtime/macros/（ADR 0100）</summary>
    public static string UserRuntimeMacrosDirectory => _paths.UserRuntimeMacrosDirectory;

    /// <summary>用户级运行时性能埋点日志: ~/.jcc/runtime/perf.log（ADR 0100）</summary>
    public static string UserRuntimePerfLogPath => _paths.UserRuntimePerfLogPath;

    /// <summary>用户级遥测分析目录: ~/.jcc/analytics/（ADR 0100）</summary>
    public static string AnalyticsDirectory => _paths.AnalyticsDirectory;

    /// <summary>项目级转储目录: {cwd}/.jcc/dumps/</summary>
    public static string DumpsDirectory => _paths.DumpsDirectory;

    /// <summary>项目级 GitHub API 缓存目录: {cwd}/.jcc/gh_cache/</summary>
    public static string GhCacheDirectory => _paths.GhCacheDirectory;

    /// <summary>项目级反思记忆目录: {cwd}/.jcc/reflexion/</summary>
    public static string ReflexionDirectory => _paths.ReflexionDirectory;

    /// <summary>项目级诊断目录: {cwd}/.jcc/diag/</summary>
    public static string DiagDirectory => _paths.DiagDirectory;

    /// <summary>项目级团队记忆目录: {cwd}/.jcc/memory/</summary>
    public static string MemoryDirectory => _paths.MemoryDirectory;

    /// <summary>项目级 TODO 目录: {cwd}/.jcc/todo/</summary>
    public static string TodoDirectory => _paths.TodoDirectory;

    /// <summary>项目级模式目录: {cwd}/.jcc/mode/</summary>
    public static string ModeDirectory => _paths.ModeDirectory;

    /// <summary>项目级权限规则目录: {cwd}/.jcc/permission/</summary>
    public static string PermissionDirectory => _paths.PermissionDirectory;

    /// <summary>项目级结构化输出目录: {cwd}/.jcc/structured-output/</summary>
    public static string StructuredOutputDirectory => _paths.StructuredOutputDirectory;

    /// <summary>项目级代码索引目录: {cwd}/.jcc/code-index/</summary>
    public static string CodeIndexDirectory => _paths.CodeIndexDirectory;

    /// <summary>项目级 worktree 目录: {cwd}/.jcc/worktrees/</summary>
    public static string WorktreesDirectory => _paths.WorktreesDirectory;

    /// <summary>exe级 native DLL 目录: {AppContext.BaseDirectory}/runtime/</summary>
    public static string RuntimeDirectory => _paths.RuntimeDirectory;
}
