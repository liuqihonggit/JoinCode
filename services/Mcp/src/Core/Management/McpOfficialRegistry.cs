
namespace McpClient;

[Register]
public sealed partial class McpOfficialRegistry
{
    private readonly HttpClient _httpClient;
    [Inject] private readonly ILogger<McpOfficialRegistry>? _logger;

    private const string DefaultRegistryUrl = "https://registry.modelcontextprotocol.io";

    public McpOfficialRegistry(IHttpClientProvider httpClientProvider, ILogger<McpOfficialRegistry>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(httpClientProvider);
        _httpClient = httpClientProvider.GetClient();
        _logger = logger;
    }

    public async Task<IReadOnlyList<McpRegistryEntry>> SearchAsync(string? query = null, CancellationToken cancellationToken = default)
    {
        var url = string.IsNullOrEmpty(query)
            ? $"{DefaultRegistryUrl}/api/servers"
            : $"{DefaultRegistryUrl}/api/servers?q={Uri.EscapeDataString(query)}";

        _logger?.LogInformation("搜索 MCP 官方注册表: {Url}", url);

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = JsonSerializer.Deserialize(json, McpClientJsonContext.Default.ListMcpRegistryEntry);

            return result?.AsReadOnly() ?? (IReadOnlyList<McpRegistryEntry>)Array.Empty<McpRegistryEntry>();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "搜索 MCP 官方注册表失败");
            return Array.Empty<McpRegistryEntry>();
        }
    }

    public async Task<McpRegistryServerDetail?> GetServerDetailAsync(string serverId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverId);

        var url = $"{DefaultRegistryUrl}/api/servers/{Uri.EscapeDataString(serverId)}";

        _logger?.LogInformation("获取 MCP 服务器详情: {ServerId}", serverId);

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize(json, McpClientJsonContext.Default.McpRegistryServerDetail);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "获取 MCP 服务器详情失败: {ServerId}", serverId);
            return null;
        }
    }
}

public sealed partial class McpRegistryEntry
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("transport_type")]
    public string? TransportType { get; set; }
}

public sealed partial class McpRegistryServerDetail
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("installation")]
    public McpRegistryInstallation? Installation { get; set; }
}

public sealed partial class McpRegistryInstallation
{
    [JsonPropertyName("command")]
    public string? Command { get; set; }

    [JsonPropertyName("args")]
    public List<string>? Args { get; set; }

    [JsonPropertyName("env")]
    public Dictionary<string, string>? Env { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }
}