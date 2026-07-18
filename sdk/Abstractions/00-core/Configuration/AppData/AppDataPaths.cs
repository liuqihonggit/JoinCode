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
    string ToolResultsFolderName)
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
            ToolResultsFolderName: ResolveEnv(JccEnvVar.ToolResultsFolderName, "tool-results")
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

    private static string ResolveEnv(JccEnvVar envVar, string defaultValue)
    {
        var envValue = Environment.GetEnvironmentVariable(envVar.ToValue());
        return envValue is not null ? envValue : defaultValue;
    }
}
