namespace Core.Agents.Coordinator;

/// <summary>
/// 平台机器人适配器基类 — 封装 QQ/飞书/Discord 等外部消息平台 Bot API 的公共骨架。
/// <para>子类只需覆写 <see cref="PlatformName"/>/<see cref="AcquireTokenAsync"/>/
/// <see cref="BuildSendUrl"/>/<see cref="BuildSendContent"/>/<see cref="ExtractMessageId"/>。</para>
/// <para>公共职责：Token 获取与缓存、HTTP 发送骨架、接收通道、生命周期与释放。</para>
/// </summary>
/// <typeparam name="TConfig">平台配置类型（AppId/AppSecret/ApiBaseUrl 等）</typeparam>
public abstract class PlatformBotAdapterBase<TConfig> : IPlatformBotAdapter
{
    private readonly HttpClient _httpClient;
    private readonly TConfig _config;
    private readonly ILogger? _logger;
    private readonly Channel<PlatformMessage> _receiveChannel;
    private string? _token;
    private int _started;
    private int _disposed;

    /// <summary>
    /// 构造平台 Bot 适配器基类。
    /// </summary>
    /// <param name="config">平台配置</param>
    /// <param name="httpClient">HTTP 客户端（调用方管理生命周期）</param>
    /// <param name="logger">日志记录器</param>
    protected PlatformBotAdapterBase(TConfig config, HttpClient httpClient, ILogger? logger = null)
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

    /// <summary>平台配置（子类可访问）。</summary>
    protected TConfig Config => _config;

    /// <summary>HTTP 客户端（子类可访问，用于 token 获取等自定义请求）。</summary>
    protected HttpClient HttpClient => _httpClient;

    /// <summary>日志记录器（子类可访问）。</summary>
    protected ILogger? Logger => _logger;

    /// <inheritdoc/>
    public abstract string PlatformName { get; }

    /// <inheritdoc/>
    public bool IsConnected => Volatile.Read(ref _started) != 0 && Volatile.Read(ref _disposed) == 0;

    /// <inheritdoc/>
    public async ValueTask StartAsync(CancellationToken ct = default)
    {
        if (Interlocked.Exchange(ref _started, 1) != 0) return;
        _token = await AcquireTokenAsync(ct).ConfigureAwait(false);
        _logger?.LogInformation("{Adapter}: started, token acquired (len={Len})", GetType().Name, _token?.Length ?? 0);
    }

    /// <inheritdoc/>
    public async ValueTask<string?> SendAsync(string targetId, string text, CancellationToken ct = default)
    {
        if (_token is null) throw new InvalidOperationException("Adapter not started");
        if (Volatile.Read(ref _disposed) != 0) return null;

        using var request = new HttpRequestMessage(HttpMethod.Post, BuildSendUrl(targetId));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        request.Content = new StringContent(BuildSendContent(targetId, text), Encoding.UTF8, "application/json");

        try
        {
            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                _logger?.LogWarning("{Adapter}: send failed {Status} to {Target}", GetType().Name, response.StatusCode, targetId);
                return null;
            }
            var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            return ExtractMessageId(json);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger?.LogError(ex, "{Adapter}: send error to {Target}", GetType().Name, targetId);
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

    /// <summary>
    /// 获取平台访问令牌 — 子类实现平台特定的认证流程。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>访问令牌字符串</returns>
    protected abstract ValueTask<string> AcquireTokenAsync(CancellationToken ct);

    /// <summary>
    /// 构造发送消息的 URL — 子类实现平台特定的端点拼装。
    /// </summary>
    /// <param name="targetId">目标标识</param>
    /// <returns>完整请求 URL</returns>
    protected abstract string BuildSendUrl(string targetId);

    /// <summary>
    /// 构造发送消息的请求体 — 子类实现平台特定的 JSON 内容拼装。
    /// </summary>
    /// <param name="targetId">目标标识</param>
    /// <param name="text">消息文本</param>
    /// <returns>JSON 请求体字符串</returns>
    protected abstract string BuildSendContent(string targetId, string text);

    /// <summary>
    /// 从平台响应 JSON 中提取消息 ID — 子类实现平台特定的响应解析。
    /// </summary>
    /// <param name="json">平台响应 JSON</param>
    /// <returns>消息 ID（失败返回 null）</returns>
    protected abstract string? ExtractMessageId(string json);

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
        _receiveChannel.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }
}
