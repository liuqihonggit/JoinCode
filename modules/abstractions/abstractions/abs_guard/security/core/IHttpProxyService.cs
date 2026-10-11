namespace JoinCode.Abstractions.Security;

public interface IHttpProxyService {
    /// <summary>创建代理处理器。</summary>
    HttpClientHandler CreateProxyHandler(ProxyOptions? options = null);
    /// <summary>获取当前代理设置。</summary>
    ProxyOptions GetCurrentProxySettings();
    /// <summary>获取是否已配置代理。</summary>
    bool IsProxyConfigured { get; }
}

public sealed partial class ProxyOptions {
    /// <summary>获取代理地址。</summary>
    public string? ProxyUrl { get; init; }
    /// <summary>获取代理用户名。</summary>
    public string? ProxyUsername { get; init; }
    /// <summary>获取代理密码。</summary>
    public string? ProxyPassword { get; init; }
    /// <summary>获取绕过代理的主机列表。</summary>
    public List<string> BypassHosts { get; init; } = [];
    /// <summary>获取是否使用默认凭据。</summary>
    public bool UseDefaultCredentials { get; init; }
}