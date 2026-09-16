namespace Core.Agents.Coordinator;

/// <summary>
/// QQ 频道机器人适配器 — 通过 QQ Bot API 发送/接收消息。
/// <para>发送：POST https://api.sgroup.qq.com/channels/{{channel_id}}/messages</para>
/// <para>认证：Bearer {accessToken}（QQ Bot WebSocket 鉴权后获取）</para>
/// <para>接收：WebSocket 长连接（QQ Bot Gateway），监听 MESSAGE_CREATE 事件</para>
/// <para>配置：appId + appSecret 通过 <see cref="QqBotConfig"/> 注入</para>
/// </summary>
public sealed class QqBotAdapter : IPlatformBotAdapter
{
    private readonly HttpClient _httpClient;
    private readonly QqBotConfig _config;
    private readonly ILogger<QqBotAdapter>? _logger;
    private readonly Channel<PlatformMessage> _receiveChannel;
    private string? _accessToken;
    private int _started;
    private int _disposed;

    /// <summary>
    /// 构造 QQ Bot 适配器。
    /// </summary>
    /// <param name="config">QQ Bot 配置（appId/appSecret/基地址）</param>
    /// <param name="httpClient">HTTP 客户端（调用方管理生命周期）</param>
    /// <param name="logger">日志记录器</param>
    public QqBotAdapter(QqBotConfig config, HttpClient httpClient, ILogger<QqBotAdapter>? logger = null)
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
    public string PlatformName => "qq";

    /// <inheritdoc/>
    public bool IsConnected => Volatile.Read(ref _started) != 0 && Volatile.Read(ref _disposed) == 0;

    /// <inheritdoc/>
    public async ValueTask StartAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _started, 1) == 1) return;
        _accessToken = await GetAccessTokenAsync(ct).ConfigureAwait(false);
        _logger?.LogInformation("QqBotAdapter: started, token acquired (len={Len})", _accessToken?.Length ?? 0);
    }

    /// <inheritdoc/>
    public async ValueTask<string?> SendAsync(string targetId, string text, CancellationToken ct = default)
    {
        if (_accessToken is null) throw new InvalidOperationException("Adapter not started");
        if (Volatile.Read(ref _disposed) != 0) return null;

        var url = $"{_config.ApiBaseUrl}/channels/{targetId}/messages";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
        request.Content = new StringContent(
            $$"""{"content":{{JsonEncodedText.Encode(text)}}}""",
            Encoding.UTF8,
            "application/json");

        try
        {
            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("QqBotAdapter: send failed {Status} to {Target}", response.StatusCode, targetId);
                return null;
            }
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ExtractMessageId(json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "QqBotAdapter: send error to {Target}", targetId);
            return null;
        }
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<PlatformMessage> ReceiveAsync(CancellationToken ct = default)
        => _receiveChannel.Reader.ReadAllAsync(ct);

    /// <summary>
    /// 将平台消息注入接收通道 — 由 webhook 处理器或 WebSocket 监听器调用。
    /// </summary>
    public bool TryDeliver(PlatformMessage message) => _receiveChannel.Writer.TryWrite(message);

    private async ValueTask<string> GetAccessTokenAsync(CancellationToken ct)
    {
        var url = $"{_config.AuthBaseUrl}/app/getAppAccessToken";
        var body = $$"""{"appId":"{{_config.AppId}}","appSecret":"{{_config.AppSecret}}"}""";
        using var response = await _httpClient.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ExtractToken(json);
    }

    private static string? ExtractMessageId(string json)
    {
        var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
    }

    private static string ExtractToken(string json)
    {
        var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("access_token").GetString()!;
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
/// QQ Bot 配置 — API 凭据与端点。
/// </summary>
public sealed record QqBotConfig
{
    /// <summary>QQ Bot AppID。</summary>
    public required string AppId { get; init; }

    /// <summary>QQ Bot AppSecret。</summary>
    public required string AppSecret { get; init; }

    /// <summary>API 基地址（默认 https://api.sgroup.qq.com）。</summary>
    public string ApiBaseUrl { get; init; } = "https://api.sgroup.qq.com";

    /// <summary>鉴权基地址（默认 https://bots.qq.com）。</summary>
    public string AuthBaseUrl { get; init; } = "https://bots.qq.com";
}
