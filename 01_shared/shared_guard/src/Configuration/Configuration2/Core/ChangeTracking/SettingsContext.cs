namespace Core.Configuration;

/// <summary>
/// 设置变更上下文 — 在中间件管道中传递设置变更所需的所有数据
/// </summary>
public sealed class SettingsContext
{
    /// <summary>
    /// 文件系统 — 用于加载配置文件
    /// </summary>
    public required IFileSystem FileSystem { get; init; }

    /// <summary>
    /// 重新加载后的 SettingsJson — 由 SettingsReloadMiddleware 设置
    /// </summary>
    public SettingsJson? NewSettings { get; set; }

    /// <summary>
    /// 日志器
    /// </summary>
    public ILogger? Logger { get; init; }

    /// <summary>
    /// 遥测服务
    /// </summary>
    public ITelemetryService? TelemetryService { get; init; }
}
