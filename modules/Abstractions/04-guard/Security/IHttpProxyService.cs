namespace JoinCode.Abstractions.Security;

public interface IHttpProxyService
{
    HttpClientHandler CreateProxyHandler(ProxyOptions? options = null);
    ProxyOptions GetCurrentProxySettings();
    bool IsProxyConfigured { get; }
}

public sealed partial class ProxyOptions
{
    public string? ProxyUrl { get; init; }
    public string? ProxyUsername { get; init; }
    public string? ProxyPassword { get; init; }
    public List<string>? BypassHosts { get; init; }
    public bool UseDefaultCredentials { get; init; }
}
