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
}
