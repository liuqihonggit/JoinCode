namespace JoinCode.Cli.Output;

/// <summary>
/// XDG 标准路径解析器 — 对齐架构指南配置管理
/// 遵循 XDG Base Directory Specification:
/// - XDG_CONFIG_HOME: 用户配置目录（默认 ~/.config）
/// - XDG_DATA_HOME: 用户数据目录（默认 ~/.local/share）
/// - XDG_CACHE_HOME: 用户缓存目录（默认 ~/.cache）
///
/// 配置目录重定向统一收敛为 JCC_APP_DATA_FOLDER 单一机制（AppDataPaths 唯一数据源）；
/// 本类仅保留运行时路径解析（ADR 0055：XDG_RUNTIME_DIR/jcc/ → ~/.jcc/runtime/）。
/// </summary>
public static class XdgPathResolver {
    /// <summary>jcc 在 XDG 目录下的子目录名 — 运行时目录解析用（P1-⑦ 委托统一数据源）</summary>
    private const string AppName = BrandConstants.CliCommandName;

    // ── 运行时路径（委托到 AppDataConstants 集中定义，ADR 0055） ──

    /// <summary>
    /// 获取运行时目录 — ~/.jcc/runtime/
    /// 优先级: XDG_RUNTIME_DIR/jcc/ → ~/.jcc/runtime/
    /// ADR 0055: 从 %TEMP%/jcc/ 改为 ~/.jcc/runtime/，持久性日志不随 OS 清理临时目录丢失
    /// </summary>
    public static string GetRuntimeDirectory() {
        var xdgRuntimeDir = Environment.GetEnvironmentVariable("XDG_RUNTIME_DIR");
        if (!string.IsNullOrEmpty(xdgRuntimeDir))
            return System.IO.Path.Combine(xdgRuntimeDir, AppName);

        return JoinCode.Abstractions.Configuration.AppData.AppDataConstants.UserRuntimeDirectory;
    }

    /// <summary>获取崩溃快照目录 — ~/.jcc/runtime/crash-dumps/（ADR 0055）</summary>
    public static string GetCrashDumpsDirectory() =>
        JoinCode.Abstractions.Configuration.AppData.AppDataConstants.UserRuntimeCrashDumpsDirectory;

    /// <summary>获取错误日志路径 — ~/.jcc/runtime/jcc_error.log（ADR 0055）</summary>
    public static string GetErrorLogPath() =>
        JoinCode.Abstractions.Configuration.AppData.AppDataConstants.UserRuntimeErrorLogPath;

    /// <summary>获取 --await 超时日志路径 — ~/.jcc/runtime/jcc_await_timeout.log（ADR 0055）</summary>
    public static string GetAwaitTimeoutLogPath() =>
        JoinCode.Abstractions.Configuration.AppData.AppDataConstants.UserRuntimeAwaitTimeoutLogPath;
}