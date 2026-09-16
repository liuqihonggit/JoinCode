namespace Core.Agents.Coordinator;

/// <summary>
/// 飞书机器人适配器 — 通过飞书开放平台 Bot API 发送/接收消息。
/// <para>发送：POST https://open.feishu.cn/open-apis/im/v1/messages?receive_id_type=chat_id</para>
/// <para>认证：Bearer {tenant_access_token}（通过 app_id + app_secret 获取）</para>
/// <para>接收：事件订阅 webhook（飞书事件回调），监听 im.message.receive_v1 事件</para>
/// <para>配置：appId + appSecret 通过 <see cref="FeishuBotConfig"/> 注入</para>
/// </summary>
public sealed class FeishuBotAdapter : IPlatformBotAdapter
{
    private readonly HttpClient _httpClient;
    private readonly FeishuBotConfig _config;
    private readonly ILogger<FeishuBotAdapter>? _logger;
    private readonly Channel<PlatformMessage> _receiveChannel;
    private string? _tenantAccessToken;
    private int _started;
    private int _disposed;

    /// <summary>
    /// 构造飞书 Bot 适配器。
    /// </summary>
    /// <param name="config">飞书 Bot 配置</param>
    /// <param name="httpClient">HTTP 客户端</param>
    /// <param name="logger">日志记录器</param>
    public FeishuBotAdapter(FeishuBotConfig config, HttpClient httpClient, ILogger<FeishuBotAdapter>? logger = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger;
        _receiveChannel = Channel.CreateUnbounded<PlatformMessage>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
    }

    /// <inheritdoc/>
    public string PlatformName => "feishu";

    /// <inheritdoc/>
    public bool IsConnected => Volatile.Read(ref _started) != 0 && Volatile.Read(ref _disposed) == 0;

    /// <inheritdoc/>
    public async ValueTask StartAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;
        _tenantAccessToken = await GetTenantAccessTokenAsync(ct).ConfigureAwait(false);
        _logger?.LogInformation("FeishuBotAdapter: started, token acquired (len={Len})", _tenantAccessToken?.Length ?? 0);
    }

    /// <inheritdoc/>
    public async ValueTask<string?> SendAsync(string targetId, string text, CancellationToken ct = default)
    {
        if (_tenantAccessToken is null) throw new InvalidOperationException("Adapter not started");
        if (Volatile.Read(ref _disposed) != 0) return null;

        var url = $"{_config.ApiBaseUrl}/open-apis/im/v1/messages?receive_id_type=chat_id";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _tenantAccessToken);
        var content = $$"""{"receive_id":"{{targetId}}","msg_type":"text","content":"{\"text\":\"{{JsonEncodedText.Encode(text)}}\"}"}""";
        request.Content = new StringContent(content, Encoding.UTF8, "application/json");

        try
        {
            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("FeishuBotAdapter: send failed {Status} to {Target}", response.StatusCode, targetId);
                return null;
            }
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ExtractMessageId(json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "FeishuBotAdapter: send error to {Target}", targetId);
            return null;
        }
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<PlatformMessage> ReceiveAsync(CancellationToken ct = default)
        => _receiveChannel.Reader.ReadAllAsync(ct);

    /// <summary>
    /// 将平台消息注入接收通道 — 由飞书事件回调 webhook 处理器调用。
    /// </summary>
    public bool TryDeliver(PlatformMessage message) => _receiveChannel.Writer.TryWrite(message);

    private async ValueTask<string> GetTenantAccessTokenAsync(CancellationToken ct)
    {
        var url = $"{_config.ApiBaseUrl}/open-apis/auth/v3/tenant_access_token/internal";
        var body = $$"""{"app_id":"{{_config.AppId}}","app_secret":"{{_config.AppSecret}}"}""";
        using var response = await _httpClient.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("tenant_access_token").GetString()!;
    }

    private static string? ExtractMessageId(string json)
    {
        var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("data", out var data)
            && data.TryGetProperty("message_id", out var id))
        {
            return id.GetString();
        }
        return null;
    }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return ValueTask.CompletedTask;
        _receiveChannel.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// 飞书 Bot 配置 — API 凭据与端点。
/// </summary>
public sealed record FeishuBotConfig
{
    /// <summary>飞书 App ID。</summary>
    public required string AppId { get; init; }

    /// <summary>飞书 App Secret。</summary>
    public required string AppSecret { get; init; }

    /// <summary>API 基地址（默认 https://open.feishu.cn）。</summary>
    public string ApiBaseUrl { get; init; } = "https://open.feishu.cn";
}
