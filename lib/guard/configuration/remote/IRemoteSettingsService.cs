
namespace Core.Configuration.Remote;

public interface IRemoteSettingsService : IDisposable
{
    Task<string?> GetSettingAsync(string key, CancellationToken cancellationToken = default);
    Task<T?> GetSettingAsync<T>(string key, T? defaultValue = default, CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, ManagedSetting>> GetAllSettingsAsync(CancellationToken cancellationToken = default);
    Task RefreshAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<string, string>> GetMergedSettingsAsync(Dictionary<string, string> localSettings, CancellationToken cancellationToken = default);
}

public sealed class SettingChangedEventArgs : EventArgs
{
    public required string Key { get; init; }
    public string? OldValue { get; init; }
    public string? NewValue { get; init; }
    public required SettingScope Scope { get; init; }
}
