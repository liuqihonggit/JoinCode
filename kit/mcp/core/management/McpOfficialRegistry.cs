
namespace McpClient;

/// <summary>
/// MCP 官方注册表客户端 — 搜索和查询 MCP 官方注册表中的服务器信息
/// </summary>
[Register(typeof(McpOfficialRegistry), ServiceLifetime.Singleton)]
public sealed partial class McpOfficialRegistry : ServiceEntity
{
    private readonly IResilientHttpClientProvider _resilientProvider;
    private readonly ILogger<McpOfficialRegistry>? _logger;

    /// <summary>
    /// 初始化 MCP 官方注册表客户端
    /// </summary>
    /// <param name="resilientProvider">支持重试/熔断的 HTTP 客户端提供者</param>
    /// <param name="logger">日志记录器（可选）</param>
    public McpOfficialRegistry(IResilientHttpClientProvider resilientProvider, ILogger<McpOfficialRegistry>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(resilientProvider);
        _resilientProvider = resilientProvider;
        _logger = logger;
    }

    /// <summary>
    /// 异步搜索 MCP 官方注册表，返回匹配的服务器列表
    /// </summary>
    /// <param name="query">搜索关键词（可选，null 或空表示返回全部）</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>匹配的注册表条目只读列表；失败时返回空列表</returns>
    public async Task<IReadOnlyList<McpRegistryEntry>> SearchAsync(string? query = null, CancellationToken cancellationToken = default)
    {
        var registryUrl = JccEndpointsResolver.McpOfficialRegistry;
        var url = string.IsNullOrEmpty(query)
            ? $"{registryUrl}/api/servers"
            : $"{registryUrl}/api/servers?q={Uri.EscapeDataString(query)}";

        _logger?.LogInformation("搜索 MCP 官方注册表: {Url}", url);

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            var response = await _resilientProvider.SendResilientAsync(request, "McpRegistry.Search", cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            var result = RelaxedJsonSerializer.Deserialize(json, McpClientJsonContext.Default.ListMcpRegistryEntry);

            return result?.AsReadOnly() ?? (IReadOnlyList<McpRegistryEntry>)Array.Empty<McpRegistryEntry>();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "搜索 MCP 官方注册表失败");
            return Array.Empty<McpRegistryEntry>();
        }
    }

    /// <summary>
    /// 异步获取指定 MCP 服务器的详细信息（含安装配置）
    /// </summary>
    /// <param name="serverId">服务器标识</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>服务器详情；失败时返回 null</returns>
    public async Task<McpRegistryServerDetail?> GetServerDetailAsync(string serverId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverId);

        var url = $"{JccEndpointsResolver.McpOfficialRegistry}/api/servers/{Uri.EscapeDataString(serverId)}";

        _logger?.LogInformation("获取 MCP 服务器详情: {ServerId}", serverId);

        try
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            var response = await _resilientProvider.SendResilientAsync(request, "McpRegistry.GetServerDetail", cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return RelaxedJsonSerializer.Deserialize(json, McpClientJsonContext.Default.McpRegistryServerDetail);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "获取 MCP 服务器详情失败: {ServerId}", serverId);
            return null;
        }
    }
}

/// <summary>
/// MCP 注册表条目 — 注册表搜索结果的单条记录
/// </summary>
public sealed partial class McpRegistryEntry
{
    /// <summary>服务器标识</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>服务器名称</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>服务器描述</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>作者</summary>
    [JsonPropertyName("author")]
    public string? Author { get; set; }

    /// <summary>服务器 URL</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }

    /// <summary>传输类型（stdio/http/sse 等）</summary>
    [JsonPropertyName("transport_type")]
    public string? TransportType { get; set; }
}

/// <summary>
/// MCP 注册表服务器详情 — 包含安装配置的完整服务器信息
/// </summary>
public sealed partial class McpRegistryServerDetail
{
    /// <summary>服务器标识</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>服务器名称</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    /// <summary>服务器描述</summary>
    [JsonPropertyName("description")]
    public string? Description { get; set; }

    /// <summary>作者</summary>
    [JsonPropertyName("author")]
    public string? Author { get; set; }

    /// <summary>安装配置</summary>
    [JsonPropertyName("installation")]
    public McpRegistryInstallation? Installation { get; set; }
}

/// <summary>
/// MCP 注册表安装配置 — 描述如何安装和启动服务器
/// </summary>
public sealed partial class McpRegistryInstallation
{
    /// <summary>启动命令</summary>
    [JsonPropertyName("command")]
    public string? Command { get; set; }

    /// <summary>命令参数列表</summary>
    [JsonPropertyName("args")]
    public List<string> Args { get; set; } = [];

    /// <summary>环境变量字典</summary>
    [JsonPropertyName("env")]
    public Dictionary<string, string> Env { get; set; } = [];

    /// <summary>安装类型</summary>
    [JsonPropertyName("type")]
    public string? Type { get; set; }

    /// <summary>服务器 URL（远程类型使用）</summary>
    [JsonPropertyName("url")]
    public string? Url { get; set; }
}