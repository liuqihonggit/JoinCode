namespace JoinCode.Abstractions.Utils;

/// <summary>
/// 聊天命令名称枚举 — 源码生成器自动生成 ChatCommandNameConstants + ChatCommandNameExtensions
/// </summary>
public enum ChatCommandName
{
    // 会话
    [EnumValue("resume")] Resume,
    [EnumValue("exit")] Exit,
    [EnumValue("quit")] Quit,
    [EnumValue("clear")] Clear,
    [EnumValue("compact")] Compact,
    [EnumValue("rewind")] Rewind,
    [EnumValue("fork")] Fork,
    [EnumValue("branch")] Branch,
    [EnumValue("session")] Session,
    [EnumValue("rename")] Rename,
    [EnumValue("history")] History,
    [EnumValue("brief")] Brief,

    // 模型
    [EnumValue("model")] Model,
    [EnumValue("fast")] Fast,
    [EnumValue("effort")] Effort,
    [EnumValue("thinkback")] Thinkback,
    [EnumValue("passes")] Passes,
    [EnumValue("output-style")] OutputStyle,
    [EnumValue("cost")] Cost,
    [EnumValue("rate-limit-options")] RateLimitOptions,
    [EnumValue("extra-usage")] ExtraUsage,

    // 代码
    [EnumValue("review")] Review,
    [EnumValue("diff")] Diff,
    [EnumValue("files")] Files,
    [EnumValue("execute")] Execute,
    [EnumValue("analyze")] Analyze,
    [EnumValue("add-dir")] AddDir,
    [EnumValue("security-review")] SecurityReview,
    [EnumValue("commit")] Commit,
    [EnumValue("worktree")] Worktree,

    // 工具
    [EnumValue("tools")] Tools,
    [EnumValue("mcp")] Mcp,
    [EnumValue("hooks")] Hooks,
    [EnumValue("skills")] Skills,
    [EnumValue("plugin")] Plugin,
    [EnumValue("install")] Install,
    [EnumValue("install-github-app")] InstallGitHubApp,

    // 配置
    [EnumValue("config")] Config,
    [EnumValue("theme")] Theme,
    [EnumValue("color")] Color,
    [EnumValue("vim")] Vim,
    [EnumValue("keybindings")] Keybindings,
    [EnumValue("env")] Env,
    [EnumValue("sandbox-toggle")] SandboxToggle,
    [EnumValue("permissions")] Permissions,
    [EnumValue("init")] Init,
    [EnumValue("doctor")] Doctor,
    [EnumValue("reset-config")] ResetConfig,

    // 信息
    [EnumValue("status")] Status,
    [EnumValue("usage")] Usage,
    [EnumValue("stats")] Stats,
    [EnumValue("insights")] Insights,
    [EnumValue("version")] Version,
    [EnumValue("help")] Help,
    [EnumValue("release-notes")] ReleaseNotes,
    [EnumValue("context")] Context,

    // 系统
    [EnumValue("export")] Export,
    [EnumValue("copy")] Copy,
    [EnumValue("summary")] Summary,
    [EnumValue("statusline")] Statusline,
    [EnumValue("heapdump")] Heapdump,
    [EnumValue("tag")] Tag,
    [EnumValue("workflows")] Workflows,
    [EnumValue("upgrade")] Upgrade,

    // 认证
    [EnumValue("login")] Login,
    [EnumValue("logout")] Logout,
    [EnumValue("trust")] Trust,
    [EnumValue("oauth-refresh")] OauthRefresh,
    [EnumValue("privacy-settings")] PrivacySettings,

    // 智能体
    [EnumValue("plan")] Plan,
    [EnumValue("ultraplan")] Ultraplan,
    [EnumValue("memory")] Memory,
    [EnumValue("agents")] Agents,
    [EnumValue("advisor")] Advisor,
    [EnumValue("buddy")] Buddy,
    [EnumValue("generate")] Generate,
    [EnumValue("assistant")] Assistant,

    // 任务
    [EnumValue("tasks")] Tasks,
    [EnumValue("goal")] Goal,
    [EnumValue("proactive")] Proactive,

    // 连接
    [EnumValue("bridge")] Bridge,
    [EnumValue("bridge-kick")] BridgeKick,
    [EnumValue("peers")] Peers,

    // 平台
    [EnumValue("chrome")] Chrome,
    [EnumValue("ide")] Ide,
    [EnumValue("desktop")] Desktop,
    [EnumValue("mobile")] Mobile,

    // 社交
    [EnumValue("btw")] Btw,
    [EnumValue("feedback")] Feedback,
    [EnumValue("share")] Share,
    [EnumValue("voice")] Voice,
    [EnumValue("stickers")] Stickers,

    // 其他
    [EnumValue("simple")] Simple,
}
