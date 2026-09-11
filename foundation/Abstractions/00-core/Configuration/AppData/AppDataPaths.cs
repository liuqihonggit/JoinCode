namespace JoinCode.Abstractions.Configuration.AppData;

/// <summary>
/// 应用数据路径 — 不可变记录类，替代 AppDataConstants 的可变静态属性
/// 通过工厂方法创建，支持环境变量覆盖和 DI 注入
/// 测试中注入不同实例替代修改全局状态，消除串行化需求
/// </summary>
public sealed record AppDataPaths(
    string AppDataFolder,
    string CredentialsFileName,
    string AuthFileName,
    string SettingsFileName,
    string GlobalConfigFileName,
    string RulesFolderName,
    string ProjectRulesFileName,
    string ScheduledTasksFileName,
    string TeamsFolderName,
    string TasksFolderName,
    string WorktreesFolderName,
    string AgentsFolderName,
    string ThemeFileName,
    string TrustedFoldersFileName,
    string SessionsFolderName,
    string SessionMetaFileName,
    string CommandsFolderName,
    string MailboxFolderName,
    string FileHistoryFolderName,
    string PlansFolderName,
    string ToolResultsFolderName,
    string McpFolderName,
    string McpConnectionsFileName,
    string McpAuthFileName)
{
    /// <summary>
    /// 默认实例 — 从环境变量解析，等价于原 AppDataConstants 的默认行为
    /// </summary>
    public static AppDataPaths Default { get; } = FromEnvironment();

    /// <summary>
    /// 从环境变量解析所有路径
    /// </summary>
    public static AppDataPaths FromEnvironment()
    {
        return new AppDataPaths(
            AppDataFolder: ResolveEnv(JccEnvVar.AppDataFolder, ".jcc"),
            CredentialsFileName: ResolveEnv(JccEnvVar.CredentialsFileName, "credentials.json"),
            AuthFileName: ResolveEnv(JccEnvVar.AuthFileName, "auth.json"),
            SettingsFileName: ResolveEnv(JccEnvVar.SettingsFileName, "settings.json"),
            GlobalConfigFileName: ResolveEnv(JccEnvVar.GlobalConfigFileName, "global.json"),
            RulesFolderName: ResolveEnv(JccEnvVar.RulesFolderName, "rules"),
            ProjectRulesFileName: ResolveEnv(JccEnvVar.ProjectRulesFileName, "project_rules.md"),
            ScheduledTasksFileName: ResolveEnv(JccEnvVar.ScheduledTasksFileName, "scheduled_tasks.json"),
            TeamsFolderName: ResolveEnv(JccEnvVar.TeamsFolderName, "teams"),
            TasksFolderName: ResolveEnv(JccEnvVar.TasksFolderName, "tasks"),
            WorktreesFolderName: ResolveEnv(JccEnvVar.WorktreesFolderName, "worktrees"),
            AgentsFolderName: ResolveEnv(JccEnvVar.AgentsFolderName, "agents"),
            ThemeFileName: ResolveEnv(JccEnvVar.ThemeFileName, "theme.json"),
            TrustedFoldersFileName: ResolveEnv(JccEnvVar.TrustedFoldersFileName, "trusted_folders.json"),
            SessionsFolderName: ResolveEnv(JccEnvVar.SessionsFolderName, "sessions"),
            SessionMetaFileName: ResolveEnv(JccEnvVar.SessionMetaFileName, "session.meta.json"),
            CommandsFolderName: ResolveEnv(JccEnvVar.CommandsFolderName, "commands"),
            MailboxFolderName: ResolveEnv(JccEnvVar.MailboxFolderName, "mailbox"),
            FileHistoryFolderName: ResolveEnv(JccEnvVar.FileHistoryFolderName, "file-history"),
            PlansFolderName: ResolveEnv(JccEnvVar.PlansFolderName, "plans"),
            ToolResultsFolderName: ResolveEnv(JccEnvVar.ToolResultsFolderName, "tool-results"),
            McpFolderName: ResolveEnv(JccEnvVar.McpFolderName, "mcp"),
            McpConnectionsFileName: ResolveEnv(JccEnvVar.McpConnectionsFileName, "connections.json"),
            McpAuthFileName: ResolveEnv(JccEnvVar.McpAuthFileName, "auth.json")
        );
    }

    /// <summary>
    /// 创建测试用的自定义实例
    /// </summary>
    public static AppDataPaths CreateForTest(
        string? appDataFolder = null,
        string? settingsFileName = null,
        string? authFileName = null)
    {
        var defaults = Default;
        return defaults with
        {
            AppDataFolder = appDataFolder ?? defaults.AppDataFolder,
            SettingsFileName = settingsFileName ?? defaults.SettingsFileName,
            AuthFileName = authFileName ?? defaults.AuthFileName,
        };
    }

    /// <summary>
    /// 获取 .jcc 目录的完整路径 — 统一使用 UserProfile（~/.jcc/）
    /// </summary>
    public string JccDirectory
    {
        get
        {
            if (Path.IsPathRooted(AppDataFolder))
                return AppDataFolder;

            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                AppDataFolder);
        }
    }

    /// <summary>
    /// 获取 auth.json 的完整路径
    /// </summary>
    public string AuthFilePath => Path.Combine(JccDirectory, AuthFileName);

    /// <summary>
    /// 获取 settings.json 的完整路径
    /// </summary>
    public string SettingsFilePath => Path.Combine(JccDirectory, SettingsFileName);

    public string GlobalConfigFilePath => Path.Combine(JccDirectory, GlobalConfigFileName);

    /// <summary>
    /// 获取 tokens 目录的完整路径
    /// </summary>
    public string TokensDirectory => Path.Combine(JccDirectory, "tokens");

    /// <summary>
    /// 项目级配置目录名（如 .jcc）
    /// </summary>
    public string ProjectConfigFolderName => AppDataFolder;

    /// <summary>
    /// 项目级本地设置文件相对路径
    /// </summary>
    public string LocalSettingsRelativePath => $"{AppDataFolder}/settings.local.json";

    /// <summary>
    /// 项目级 worktree 目录名 — WorktreeFolderName 别名，对齐 WorkflowConstants.Paths.WorktreeFolderName
    /// </summary>
    public string WorktreeFolderName => WorktreesFolderName;

    // === 用户级路径 (~/.jcc/) — 跨项目共享、用户全局状态 ===

    /// <summary>用户级 cron 定时任务目录: ~/.jcc/cron-tasks/</summary>
    public string CronTasksDirectory => Path.Combine(JccDirectory, "cron-tasks");

    /// <summary>用户级记忆存储目录: ~/.jcc/memdir/</summary>
    public string MemdirDirectory => Path.Combine(JccDirectory, "memdir");

    /// <summary>用户级成本跟踪文件: ~/.jcc/cost-tracking.json</summary>
    public string CostTrackingFilePath => Path.Combine(JccDirectory, "cost-tracking.json");

    /// <summary>用户级成本跟踪目录: ~/.jcc/costs/</summary>
    public string CostsDirectory => Path.Combine(JccDirectory, "costs");

    /// <summary>用户级会话历史目录: ~/.jcc/sessions/</summary>
    public string SessionsDirectory => Path.Combine(JccDirectory, SessionsFolderName);

    /// <summary>用户级文件编辑历史目录: ~/.jcc/file-history/</summary>
    public string FileHistoryDirectory => Path.Combine(JccDirectory, FileHistoryFolderName);

    /// <summary>用户级大文本粘贴缓存目录: ~/.jcc/paste-cache/</summary>
    public string PasteCacheDirectory => Path.Combine(JccDirectory, "paste-cache");

    /// <summary>用户级 Shell 快照目录: ~/.jcc/shell-snapshots/</summary>
    public string ShellSnapshotsDirectory => Path.Combine(JccDirectory, "shell-snapshots");

    /// <summary>用户级计划文件目录: ~/.jcc/plans/</summary>
    public string PlansDirectory => Path.Combine(JccDirectory, PlansFolderName);

    /// <summary>用户级任务存储目录: ~/.jcc/tasks/</summary>
    public string TasksDirectory => Path.Combine(JccDirectory, TasksFolderName);

    /// <summary>用户级团队配置目录: ~/.jcc/teams/</summary>
    public string TeamsDirectory => Path.Combine(JccDirectory, TeamsFolderName);

    /// <summary>用户级 MCP 配置目录: ~/.jcc/mcp/</summary>
    public string McpDirectory => Path.Combine(JccDirectory, McpFolderName);

    /// <summary>用户级工具模板目录: ~/.jcc/tool-templates/</summary>
    public string ToolTemplatesDirectory => Path.Combine(JccDirectory, "tool-templates");

    /// <summary>用户级 Agent 状态目录: ~/.jcc/agents/</summary>
    public string AgentsDirectory => Path.Combine(JccDirectory, AgentsFolderName);

    /// <summary>用户级源代码克隆目录: ~/.jcc/source/</summary>
    public string SourceDirectory => Path.Combine(JccDirectory, "source");

    /// <summary>用户级关键词配置文件: ~/.jcc/keyword-sections.json</summary>
    public string KeywordSectionsFilePath => Path.Combine(JccDirectory, "keyword-sections.json");

    /// <summary>用户级 LSP 服务器配置文件: ~/.jcc/lsp-servers.json</summary>
    public string LspServersFilePath => Path.Combine(JccDirectory, "lsp-servers.json");

    /// <summary>用户级 GUI 偏好文件: ~/.jcc/gui-preferences.json</summary>
    public string GuiPreferencesFilePath => Path.Combine(JccDirectory, "gui-preferences.json");

    /// <summary>用户级引导完成标记文件: ~/.jcc/onboarding_complete.json</summary>
    public string OnboardingCompleteFilePath => Path.Combine(JccDirectory, "onboarding_complete.json");

    // === 项目级路径 ({cwd}/.jcc/) — 项目隔离、随项目走 ===

    /// <summary>项目级 .jcc 目录: {cwd}/.jcc/</summary>
    public string ProjectJccDirectory => Path.Combine(Environment.CurrentDirectory, AppDataFolder);

    /// <summary>项目级工作流状态目录: {cwd}/.jcc/workflow-states/</summary>
    public string WorkflowStatesDirectory => Path.Combine(ProjectJccDirectory, "workflow-states");

    /// <summary>项目级运行时任务目录: {cwd}/.jcc/runtime-tasks/</summary>
    public string RuntimeTasksDirectory => Path.Combine(ProjectJccDirectory, "runtime-tasks");

    /// <summary>项目级目标状态目录: {cwd}/.jcc/goal-state/</summary>
    public string GoalStateDirectory => Path.Combine(ProjectJccDirectory, "goal-state");

    /// <summary>项目级 .env 配置目录: {cwd}/.jcc/.env/</summary>
    public string DotEnvDirectory => Path.Combine(ProjectJccDirectory, ".env");

    /// <summary>项目级更新内容目录: {cwd}/.jcc/UpdateContent/</summary>
    public string UpdateContentDirectory => Path.Combine(ProjectJccDirectory, "UpdateContent");

    /// <summary>项目级转储目录: {cwd}/.jcc/dumps/</summary>
    public string DumpsDirectory => Path.Combine(ProjectJccDirectory, "dumps");

    /// <summary>项目级 GitHub API 缓存目录: {cwd}/.jcc/gh_cache/</summary>
    public string GhCacheDirectory => Path.Combine(ProjectJccDirectory, "gh_cache");

    /// <summary>项目级反思记忆目录: {cwd}/.jcc/reflexion/</summary>
    public string ReflexionDirectory => Path.Combine(ProjectJccDirectory, "reflexion");

    /// <summary>项目级诊断目录: {cwd}/.jcc/diag/</summary>
    public string DiagDirectory => Path.Combine(ProjectJccDirectory, "diag");

    /// <summary>项目级团队记忆目录: {cwd}/.jcc/memory/</summary>
    public string MemoryDirectory => Path.Combine(ProjectJccDirectory, "memory");

    /// <summary>项目级 TODO 目录: {cwd}/.jcc/todo/</summary>
    public string TodoDirectory => Path.Combine(ProjectJccDirectory, "todo");

    /// <summary>项目级模式目录: {cwd}/.jcc/mode/</summary>
    public string ModeDirectory => Path.Combine(ProjectJccDirectory, "mode");

    /// <summary>项目级权限规则目录: {cwd}/.jcc/permission/</summary>
    public string PermissionDirectory => Path.Combine(ProjectJccDirectory, "permission");

    /// <summary>项目级结构化输出目录: {cwd}/.jcc/structured-output/</summary>
    public string StructuredOutputDirectory => Path.Combine(ProjectJccDirectory, "structured-output");

    /// <summary>项目级代码索引目录: {cwd}/.jcc/code-index/</summary>
    public string CodeIndexDirectory => Path.Combine(ProjectJccDirectory, "code-index");

    /// <summary>项目级 worktree 目录: {cwd}/.jcc/worktrees/</summary>
    public string WorktreesDirectory => Path.Combine(ProjectJccDirectory, WorktreesFolderName);

    // === exe级路径 (AppContext.BaseDirectory) — 跟 exe 走,exe 升级时同步 ===

    /// <summary>exe级 native DLL 目录: {AppContext.BaseDirectory}/runtime/</summary>
    public string RuntimeDirectory => Path.Combine(AppContext.BaseDirectory, "runtime");

    private static string ResolveEnv(JccEnvVar envVar, string defaultValue)
    {
        var envValue = Environment.GetEnvironmentVariable(envVar.ToValue());
        return envValue is not null ? envValue : defaultValue;
    }
}
