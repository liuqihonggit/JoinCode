namespace JoinCode.Abstractions.Interfaces;

/// <summary>
/// 配置变更事件参数
/// </summary>
public sealed class SettingChangeEventArgs : EventArgs
{
    public required string Key { get; init; }
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public SettingSource Source { get; init; } = SettingSource.UserSettings;

    /// <summary>
    /// 热更新同步键 — 对齐 TS 版 appStateKey
    /// 当设置项定义了 appStateKey 时，写入成功后消费者应同步到对应 AppState 字段
    /// 例如: verbose → Verbose, model → MainLoopModel, alwaysThinkingEnabled → ThinkingEnabled
    /// </summary>
    public string? AppStateKey { get; init; }
}

/// <summary>
/// 配置服务接口 - 提供配置项的读取和设置功能
/// 对齐 TS 版 ConfigTool: 支持双存储源(global/settings) + 变更通知
/// </summary>
public interface IConfigurationService
{
    /// <summary>
    /// 设置变更事件（对齐 TS 版 appStateKey 热更新同步）
    /// </summary>
    event EventHandler<SettingChangeEventArgs>? SettingChanged;

    /// <summary>
    /// 获取配置项值（默认从 settings 源读取）
    /// </summary>
    Task<string?> GetAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取配置项值（指定存储源）
    /// 所有源统一从 settings.json 读取
    /// </summary>
    Task<string?> GetAsync(string key, SettingSource source, CancellationToken cancellationToken = default);

    /// <summary>
    /// 设置配置项值（默认写入 settings 源）
    /// </summary>
    Task<bool> SetAsync(string key, string value, CancellationToken cancellationToken = default);

    /// <summary>
    /// 设置配置项值（指定存储源）
    /// 对齐 TS 版 updateSettingsForSource — 统一写入 settings.json
    /// </summary>
    Task<bool> SetAsync(string key, string value, SettingSource source, CancellationToken cancellationToken = default);

    /// <summary>
    /// 设置配置项值（指定存储源和热更新键）
    /// 对齐 TS 版 appStateKey — 写入成功后同步到对应 AppState 字段
    /// </summary>
    Task<bool> SetAsync(string key, string value, SettingSource source, string? appStateKey, CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除配置项
    /// </summary>
    Task<bool> RemoveAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 获取所有配置项
    /// </summary>
    Task<Dictionary<string, string>> GetAllAsync(CancellationToken cancellationToken = default);
}
