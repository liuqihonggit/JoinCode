
namespace Core.Hub.Bootstrap;

public sealed partial class AgentHubBootstrap {
    private readonly ILogger<AgentHubBootstrap>? _logger;
    private readonly ConfigLoader _configLoader;
    private readonly IFileSystem _fileSystem;
    private AgentHubServer? _server;

    /// <summary>获取已启动的服务器实例（null 表示未启动）。</summary>
    public AgentHubServer? Server => _server;

    /// <summary>获取服务器是否已由本实例启动。</summary>
    public bool IsServerStarted => _server?.IsRunning ?? false;

    /// <summary>
    /// 构造 AgentHubBootstrap — 负责启动时检测/启动 AgentHubServer。
    /// </summary>
    /// <param name="configLoader">配置加载器（复用现有 ConfigLoader）。</param>
    /// <param name="fileSystem">文件系统抽象。</param>
    /// <param name="logger">日志记录器。</param>
    public AgentHubBootstrap(
        ConfigLoader configLoader,
        IFileSystem fileSystem,
        ILogger<AgentHubBootstrap>? logger = null) {
        _configLoader = configLoader ?? throw new ArgumentNullException(nameof(configLoader));
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _logger = logger;
    }

    /// <summary>
    /// 确保 AgentHubServer 已启动 — 如果端口可用则启动服务器，否则连接到已运行的服务器。
    /// </summary>
    /// <param name="port">监听端口，默认 3457。</param>
    /// <param name="host">监听主机，默认 localhost。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>连接信息（包含 endpoint 和是否为服务器拥有者）。</returns>
    public async Task<HubConnectionInfo> EnsureServerAsync(
        int port = 3457,
        string host = "localhost",
        CancellationToken ct = default) {
        if (!await IsPortAvailableAsync(host, port, ct).ConfigureAwait(false)) {
            _logger?.LogInformation("[AgentHubBootstrap] 端口 {Port} 已被占用，连接到已运行的 AgentHubServer", port);
            return new HubConnectionInfo {
                Endpoint = $"http://{host}:{port}",
                IsOwner = false,
                Port = port
            };
        }

        _logger?.LogInformation("[AgentHubBootstrap] 端口 {Port} 可用，启动 AgentHubServer", port);

        var settings = await ConfigLoader.LoadSettingsJsonAsync(_fileSystem, ct).ConfigureAwait(false);
        settings ??= new SettingsJson();

        var providerRegistry = ProviderDefinitionRegistry.Create(
            new ModelConfigLoader(),
            _fileSystem);

        _server = new AgentHubServer(
            settings,
            providerRegistry,
            port,
            host,
            _logger as ILogger<AgentHubServer>);

        _server.Start();

        return new HubConnectionInfo {
            Endpoint = $"http://{host}:{port}",
            IsOwner = true,
            Port = port
        };
    }

    private static async Task<bool> IsPortAvailableAsync(string host, int port, CancellationToken ct) {
        try {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(1) };
            var response = await client.GetAsync($"http://{host}:{port}/health", ct).ConfigureAwait(false);
            return !response.IsSuccessStatusCode;
        } catch {
            return true;
        }
    }

    /// <summary>停止由本实例启动的服务器。</summary>
    /// <param name="ct">取消令牌。</param>
    public async Task StopAsync(CancellationToken ct = default) {
        if (_server != null && _server.IsRunning) {
            await _server.StopAsync(ct).ConfigureAwait(false);
        }
    }
}

/// <summary>AgentHubServer 连接信息。</summary>
public sealed class HubConnectionInfo {
    /// <summary>服务器 endpoint 地址。</summary>
    [JsonPropertyName("endpoint")]
    public string Endpoint { get; init; } = string.Empty;

    /// <summary>是否为服务器拥有者（即由本实例启动）。</summary>
    [JsonPropertyName("is_owner")]
    public bool IsOwner { get; init; }

    /// <summary>服务器端口。</summary>
    [JsonPropertyName("port")]
    public int Port { get; init; }
}
