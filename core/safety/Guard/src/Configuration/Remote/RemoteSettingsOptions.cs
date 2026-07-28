
namespace Core.Configuration.Remote;

public sealed class RemoteSettingsOptions : IRemoteRefreshOptions
{
    public const string SectionName = "RemoteSettings";

    public string ApiEndpoint { get; set; } = string.Empty;
    public string ClientKey { get; set; } = string.Empty;
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(15);
    public TimeSpan CacheExpiration { get; set; } = TimeSpan.FromMinutes(20);
    public bool EnableCache { get; set; } = true;
    public bool EnableNotifications { get; set; } = true;
    public bool MergeWithLocal { get; set; } = true;
}
