
namespace Core.Policy;

public sealed class RemotePolicyOptions : IRemoteRefreshOptions
{
    public const string SectionName = "RemotePolicy";

    public string ApiEndpoint { get; set; } = string.Empty;
    public string ClientKey { get; set; } = string.Empty;
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromMinutes(10);
    public TimeSpan CacheExpiration { get; set; } = TimeSpan.FromMinutes(15);
    public bool EnableCache { get; set; } = true;
    public bool EnableNotifications { get; set; } = true;
}
