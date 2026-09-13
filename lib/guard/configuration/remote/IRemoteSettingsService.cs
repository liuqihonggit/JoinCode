
namespace Core.Configuration.Remote;

/// <summary>
/// 远程设置服务接口 — 提供远程托管设置的读取、刷新与本地合并能力
/// </summary>
public interface IRemoteSettingsService : IDisposable
{
    /// <summary>
    /// 异步按键获取设置原始字符串值
    /// </summary>
    /// <param name="key">设置键名</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>设置值，不存在时返回 null</returns>
    Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步按键获取设置并反序列化为指定类型，不存在时返回默认值
    /// </summary>
    /// <typeparam name="T">目标类型</typeparam>
    /// <param name="key">设置键名</param>
    /// <param name="defaultValue">默认值</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>反序列化后的设置值</returns>
    Task<T?> GetSettingAsync<T>(string key, T? defaultValue = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步获取全部托管设置
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>键到托管设置项的只读字典</returns>
    Task<IReadOnlyDictionary<string, ManagedSetting>> GetAllSettingsAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步从远程刷新本地缓存的设置
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    Task RefreshAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 异步将远程设置与本地设置合并，远程优先
    /// </summary>
    /// <param name="localSettings">本地设置字典</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>合并后的键值只读字典</returns>
    Task<IReadOnlyDictionary<string, string>> GetMergedSettingsAsync(Dictionary<string, string> localSettings, CancellationToken cancellationToken = default);
}

/// <summary>
/// 设置变更事件参数 — 当托管设置发生变更时携带的事件数据
/// </summary>
public sealed class SettingChangedEventArgs : EventArgs
{
    /// <summary>设置键名</summary>
    public required string Key { get; init; }
    /// <summary>旧值，可为空</summary>
    public string? OldValue { get; init; }
    /// <summary>新值，可为空</summary>
    public string? NewValue { get; init; }
    /// <summary>设置作用域</summary>
    public required SettingScope Scope { get; init; }
}
