namespace JoinCode.Abstractions.Configuration.AppData;

/// <summary>
/// 工作流配置常量 - 集中管理所有默认配置值
/// </summary>
public static class WorkflowConstants
{
    /// <summary>
    /// 超时相关常量（秒）
    /// </summary>
    public static class Timeouts
    {
        public const int DefaultTimeoutSeconds = 30;
        public const int AgentTimeoutSeconds = 300;
        public const int CodeExecutionTimeoutSeconds = 10;
        public const int ToolDefaultTimeoutSeconds = 30;
        public const int BridgeRequestTimeoutSeconds = 30;
    }

    /// <summary>
    /// 重试相关常量
    /// </summary>
    public static class Retry
    {
        public const int DefaultRetryCount = 30;
        public const int MinRetryDelayMs = 5;
        public const int MaxRetryDelayMs = 100;
        public const int DefaultRetryDelayMs = 1000;
        public const int MaxDelayMs = 30000;
        public const int MaxReconnectAttempts = 10;
    }

    /// <summary>
    /// 代码执行限制常量
    /// </summary>
    public static class CodeExecution
    {
        public const int MaxMemoryMB = 100;
        public const int MaxProcesses = 100;
        public const int MaxOpenFiles = 100;
    }

    /// <summary>
    /// Bridge 客户端常量
    /// </summary>
    public static class Bridge
    {
        public const int DefaultPollingIntervalMs = 100;
        public const int DefaultErrorRetryDelayMs = 1000;
        public const int DefaultHeartbeatIntervalMs = 30000;
        public const int DefaultMessageDeduplicationCapacity = 1000;
        public const int ReconnectDelayMs = 1000;
        public const int MaxReconnectDelayMs = 30000;
        public const int DefaultQRTokenTtlMs = 300000;
    }

    /// <summary>
    /// 缓存相关常量
    /// </summary>
    public static class Cache
    {
        public const int MaxCacheItems = 1000;
        public const int ContextCacheExpirationMinutes = 30;
        public const int ToolInfoCacheExpirationMinutes = 30;
    }

    /// <summary>
    /// Worktree 相关常量
    /// </summary>
    public static class Worktree
    {
        public const int StaleTimeoutDays = 30;
        public static string DefaultWorktreesDirectory => AppDataConstants.AppDataFolder + "/" + AppDataConstants.WorktreesFolderName;
    }

    /// <summary>
    /// 工具执行相关常量
    /// </summary>
    public static class ToolExecution
    {
        public const int MaxToolResultLength = 100000;
        public const int DefaultTimeoutMs = 30000;
        public const int MaxOutputLength = 10000;
        public const int PowerShellDefaultTimeoutMs = 300000;
    }

    /// <summary>
    /// 调度相关常量
    /// </summary>
    public static class Scheduling
    {
        public const int CronCheckIntervalMs = 1000;
        public const int OneShotMinuteMod = 30;
    }

    /// <summary>
    /// 上下文压缩常量
    /// </summary>
    public static class ContextCompression
    {
        public const int DefaultTokenThreshold = 10000;
        public const int MaxReferenceEntries = 100;
        public const int MinCompressionThreshold = 100;
        public const int SummaryIntervalMs = 30000;
        public const int MaxHistorySize = 100;
        public const int LineCountThreshold = 30;
    }

    /// <summary>
    /// 分析服务常量
    /// </summary>
    public static class Analytics
    {
        public const int DefaultEventHistoryLimit = 100;
        public const int MaxEvents = 10000;
    }

    /// <summary>
    /// 预算相关常量
    /// </summary>
    public static class Budget
    {
        public const decimal DefaultMonthlyLimit = 100.0m;
        public const decimal DefaultTotalLimit = 1000.0m;
    }

    /// <summary>
    /// Dream 任务常量
    /// </summary>
    public static class Dream
    {
        public const int MaxTurns = 30;
    }

    /// <summary>
    /// 进度条常量
    /// </summary>
    public static class ProgressBar
    {
        public const int DefaultBarWidth = 30;
    }

    /// <summary>
    /// 路径和文件名常量
    /// </summary>
    public static class Paths
    {
        public const string DefaultStateFilePath = "workflow_state.json";
        public const string DefaultWebSocketEndpoint = "ws://localhost:3456/bridge";
        public const string DefaultSseEndpoint = "http://localhost:3456/sse";
        public const string LocalHost = "localhost";
        public const int DefaultBridgePort = 3456;

        /// <summary>
        /// 获取 .jcc 目录的完整路径 — 统一使用 UserProfile（~/.jcc/）
        /// 优先使用 JCC_APP_DATA_FOLDER 环境变量覆盖（测试隔离场景）
        /// 其次检查 AppDataConstants.AppDataFolder 是否为绝对路径（backing field 覆盖场景）
        /// </summary>
        public static string JccDirectory
        {
            get
            {
                var envDir = Environment.GetEnvironmentVariable(JccEnvVarConstants.AppDataFolder);
                if (!string.IsNullOrEmpty(envDir) && Path.IsPathRooted(envDir))
                    return envDir;

                var appDataFolder = AppDataConstants.AppDataFolder;
                if (Path.IsPathRooted(appDataFolder))
                    return appDataFolder;

                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    appDataFolder);
            }
        }

        /// <summary>
        /// 获取 auth.json 的完整路径
        /// </summary>
        public static string AuthFilePath => Path.Combine(JccDirectory, AppDataConstants.AuthFileName);

        /// <summary>
        /// 获取 tokens 目录的完整路径
        /// </summary>
        public static string TokensDirectory => Path.Combine(JccDirectory, "tokens");

        /// <summary>
        /// 获取 sessions 目录的完整路径 — 存储 /resume 和 --resume/--continue 恢复的会话文件
        /// </summary>
        public static string SessionsDirectory => Path.Combine(JccDirectory, "sessions");

        /// <summary>
        /// 项目级配置目录名（如 .jcc）
        /// </summary>
        public static string ProjectConfigFolderName => AppDataConstants.AppDataFolder;

        /// <summary>
        /// 项目级 worktree 目录名
        /// </summary>
        public const string WorktreeFolderName = "worktrees";

        /// <summary>
        /// 项目级本地设置文件相对路径
        /// </summary>
        public static string LocalSettingsRelativePath => $"{AppDataConstants.AppDataFolder}/settings.local.json";

        /// <summary>
        /// 获取项目级 worktree 目录的完整路径
        /// </summary>
        public static string GetProjectWorktreesDir(string gitRoot) => Path.Combine(gitRoot, ProjectConfigFolderName, WorktreeFolderName);

        /// <summary>
        /// 获取项目级 worktree 的完整路径
        /// </summary>
        public static string GetProjectWorktreePath(string gitRoot, string agentId)
        {
            var safeId = agentId.Replace("/", "+").Replace("\\", "+");
            return Path.Combine(GetProjectWorktreesDir(gitRoot), safeId);
        }
    }

    /// <summary>
    /// 限制和阈值常量
    /// </summary>
    public static class Limits
    {
        public const int CodeLengthMax = 10000;
        public const int OutputTruncateLength = 500;
        public const int JsonTruncateLength = 2000;
        public const int FileContentTruncateLength = 2000;
        public const int PreviewTextShortLength = 100;
        public const int PreviewTextMediumLength = 150;
        public const int CodeBlockCollapseThreshold = 500;
        public const int CodeLinesCollapseThreshold = 10;
        public const int LineLengthMax = 120;
        public const int FileSizeWarningBytes = 1024 * 1024; // 1MB
        public const int FileLinesWarning = 500;
        public const int ExecutionHistoryMax = 100;
        public const int BufferSizeBytes = 8192;
        public const int DockerExtraTimeoutSeconds = 15;
        public const int NotificationDurationMs = 5000;
        public const int DefaultSearchResultLimit = 100;
        public const int DefaultGrepResultLimit = 250;

        /// <summary>
        /// 搜索操作超时时间（秒），对齐 TS ripgrep 20s 超时
        /// </summary>
        public const int SearchTimeoutSeconds = 20;

        /// <summary>
        /// Grep 搜索结果最大字符数，对齐 TS GrepTool maxResultSizeChars = 20000
        /// </summary>
        public const int GrepMaxResultSizeChars = 20000;

        /// <summary>
        /// Glob 搜索结果最大字符数，对齐 TS GlobTool maxResultSizeChars = 100000
        /// </summary>
        public const int GlobMaxResultSizeChars = 100000;
    }

    /// <summary>
    /// 文件扩展名常量
    /// </summary>
    public static class FileExtensions
    {
        public const string AppSettings = "appsettings.json";
        public const string WebConfig = "web.config";
        public const string Env = ".env";
        public const string CSharp = ".cs";
        public const string Markdown = ".md";
        public const string Json = ".json";
    }

    /// <summary>
    /// 折叠分类器常量
    /// </summary>
    public static class Collapse
    {
        public const int ShortTextThreshold = 200;
        public const int LongTextThreshold = 2000;
        public const int ListItemThreshold = 10;
    }
}
