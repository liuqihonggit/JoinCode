namespace JoinCode.Abstractions.Configuration.Settings;

/// <summary>
/// 配置变更事件参数
/// </summary>
public sealed class ConfigChangeEventArgs : EventArgs
{
    /// <summary>
    /// 变更文件路径
    /// </summary>
    public required string FilePath { get; init; }

    /// <summary>
    /// 变更类型
    /// </summary>
    public required string ChangeType { get; init; }

    /// <summary>
    /// 变更时间戳
    /// </summary>
    public required DateTimeOffset Timestamp { get; init; }
}

/// <summary>
/// 配置变更通知器接口
/// </summary>
public interface IConfigChangeNotifier
{
    /// <summary>
    /// 配置文件变更事件
    /// </summary>
    event EventHandler<ConfigChangeEventArgs>? ConfigChanged;

    /// <summary>
    /// 启动配置文件监控
    /// </summary>
    /// <param name="workingDirectory">工作目录</param>
    void StartMonitoring(string workingDirectory);

    /// <summary>
    /// 停止配置文件监控
    /// </summary>
    void StopMonitoring();

    /// <summary>
    /// 标记内部写入 — 对齐 TS markInternalWrite
    /// 在自身写入配置文件前调用，5s 窗口内该文件变更不触发 ConfigChanged 事件
    /// </summary>
    /// <param name="filePath">即将写入的文件完整路径</param>
    void MarkInternalWrite(string filePath);
}

/// <summary>
/// 设置变更应用器接口 — 对齐 TS 版 applySettingsChange
/// </summary>
public interface ISettingsChangeApplier
{
    /// <summary>
    /// 手动触发设置重新加载
    /// </summary>
    Task ApplySettingsChangeAsync(CancellationToken cancellationToken = default);
}
