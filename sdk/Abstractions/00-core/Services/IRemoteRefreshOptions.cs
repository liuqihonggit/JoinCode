
namespace JoinCode.Abstractions.Services;

public interface IRemoteRefreshOptions
{
    string ApiEndpoint { get; }
    string ClientKey { get; }
    TimeSpan RefreshInterval { get; }
    TimeSpan CacheExpiration { get; }
    bool EnableCache { get; }
}
