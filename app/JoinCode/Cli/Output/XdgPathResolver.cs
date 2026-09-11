namespace JoinCode.Cli.Output;

/// <summary>
/// XDG 标准路径解析器 — 对齐架构指南配置管理
/// 遵循 XDG Base Directory Specification:
/// - XDG_CONFIG_HOME: 用户配置目录（默认 ~/.config）
/// - XDG_DATA_HOME: 用户数据目录（默认 ~/.local/share）
/// - XDG_CACHE_HOME: 用户缓存目录（默认 ~/.cache）
///
/// jcc 配置路径回退链:
/// 1. JCC_CONFIG_PATH 环境变量（最高优先级）
/// 2. XDG_CONFIG_HOME/jcc/
/// 3. ~/.jcc/（传统路径，向后兼容）
/// </summary>
public static class XdgPathResolver
{
    /// <summary>jcc 在 XDG 目录下的子目录名</summary>
    private const string AppName = "jcc";

    /// <summary>
    /// 获取配置目录 — 按优先级回退
    /// 1. JCC_CONFIG_PATH 环境变量
    /// 2. XDG_CONFIG_HOME/jcc/
    /// 3. ~/.jcc/（传统路径）
    /// </summary>
    public static string GetConfigDirectory()
    {
        // 1. JCC_CONFIG_PATH — 用户自定义
        var customPath = Environment.GetEnvironmentVariable("JCC_CONFIG_PATH");
        if (!string.IsNullOrEmpty(customPath))
            return customPath;

        // 2. XDG_CONFIG_HOME/jcc/
        var xdgConfigHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (!string.IsNullOrEmpty(xdgConfigHome))
            return System.IO.Path.Combine(xdgConfigHome, AppName);

        // 3. 传统路径 ~/.jcc/
        return System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            $".{AppName}");
    }

    /// <summary>
    /// 获取数据目录
    /// </summary>
    public static string GetDataDirectory()
    {
        var xdgDataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (!string.IsNullOrEmpty(xdgDataHome))
            return System.IO.Path.Combine(xdgDataHome, AppName);

        return System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".local", "share", AppName);
    }

    /// <summary>
    /// 获取缓存目录
    /// </summary>
    public static string GetCacheDirectory()
    {
        var xdgCacheHome = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
        if (!string.IsNullOrEmpty(xdgCacheHome))
            return System.IO.Path.Combine(xdgCacheHome, AppName);

        return System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cache", AppName);
    }

    /// <summary>
    /// 获取 settings.json 完整路径
    /// </summary>
    public static string GetSettingsPath() =>
        System.IO.Path.Combine(GetConfigDirectory(), "settings.json");

    /// <summary>
    /// 获取 auth.json 完整路径
    /// </summary>
    public static string GetAuthPath() =>
        System.IO.Path.Combine(GetConfigDirectory(), "auth.json");

    /// <summary>
    /// 获取 trusted_folders.json 完整路径
    /// </summary>
    public static string GetTrustedFoldersPath() =>
        System.IO.Path.Combine(GetConfigDirectory(), "trusted_folders.json");

    // ── 运行时路径（委托到 AppDataConstants 集中定义，ADR 0100） ──

    /// <summary>
    /// 获取运行时目录 — ~/.jcc/runtime/
    /// 优先级: XDG_RUNTIME_DIR/jcc/ → ~/.jcc/runtime/
    /// ADR 0100: 从 %TEMP%/jcc/ 改为 ~/.jcc/runtime/，持久性日志不随 OS 清理临时目录丢失
    /// </summary>
    public static string GetRuntimeDirectory()
    {
        var xdgRuntimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrEmpty(xdgRuntimeDir))
            return System.IO.Path.Combine(xdgRuntimeDir, AppName);

        return JoinCode.Abstractions.Configuration.AppData.AppDataConstants.UserRuntimeDirectory;
    }

    /// <summary>获取崩溃快照目录 — ~/.jcc/runtime/crash-dumps/（ADR 0100）</summary>
    public static string GetCrashDumpsDirectory() =>
        JoinCode.Abstractions.Configuration.AppData.AppDataConstants.UserRuntimeCrashDumpsDirectory;

    /// <summary>获取错误日志路径 — ~/.jcc/runtime/jcc_error.log（ADR 0100）</summary>
    public static string GetErrorLogPath() =>
        JoinCode.Abstractions.Configuration.AppData.AppDataConstants.UserRuntimeErrorLogPath;

    /// <summary>获取 --await 超时日志路径 — ~/.jcc/runtime/jcc_await_timeout.log（ADR 0100）</summary>
    public static string GetAwaitTimeoutLogPath() =>
        JoinCode.Abstractions.Configuration.AppData.AppDataConstants.UserRuntimeAwaitTimeoutLogPath;
}
