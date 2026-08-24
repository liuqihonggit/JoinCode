using System.Net.Http.Headers;
using System.Text;

namespace JoinCode.Transport;

/// <summary>
/// SSE 传输配置
/// </summary>
public sealed partial class SseTransportConfig
{
    /// <summary>SSE 事件流端点（GET 请求，接收服务端推送）</summary>
    public required string EventsEndpoint { get; init; }

    /// <summary>消息发送端点（POST 请求，发送消息到对端）</summary>
    public required string MessagesEndpoint { get; init; }

    /// <summary>认证 Token（可选）</summary>
    public string? AuthToken { get; init; }

    /// <summary>连接超时</summary>
    public TimeSpan ConnectTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>重连最大尝试次数</summary>
    public int MaxReconnectAttempts { get; init; } = 3;

    /// <summary>重连退避基数（毫秒）</summary>
    public int ReconnectBackoffBaseMs { get; init; } = 1000;
}

/// <summary>
/// SSE 传输实现 — 通过 SSE 接收消息，HTTP POST 发送消息
/// </summary>
/// <remarks>
/// 适用于远程操控场景：jcc.exe 暴露 SSE 端点后，外部客户端通过此传输连接
/// </remarks>
public sealed partial class SseAgentTransport : IAgentTransport
{
    private readonly SseTransportConfig _config;
    private readonly HttpClient _httpClient;
    private readonly ILogger<SseAgentTransport>? _logger;
    private readonly BufferedChannel _outputChannel = new();
    private readonly BufferedChannel _errorChannel = new();
    private readonly CancellationTokenSource _disposeCts = new();
    private Task? _sseListenTask;
    private TransportState _state;
    private int _reconnectAttempts;

    public string TransportType => "sse";

    public TransportState State
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                _state = value;
                OnStateChanged?.Invoke(this, value);
            }
        }
    }

    public event EventHandler<TransportMessageEventArgs>? OnMessage;
    public event EventHandler<TransportState>? OnStateChanged;

    private readonly IClockService _clock;

    public SseAgentTransport(
        SseTransportConfig config,
        ILogger<SseAgentTransport>? logger = null,
        IClockService? clock = null)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger;
        _clock = clock ?? SystemClockService.Instance;
        _httpClient = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        _state = TransportState.Disconnected;
    }

    public async Task ConnectAsync(CancellationToken ct = default)
    {
        if (State == TransportState.Connected) return;

        State = TransportState.Connecting;
        try
        {
            await StartSseListenerAsync(ct).ConfigureAwait(false);
            State = TransportState.Connected;
            _logger?.LogInformation("[SseTransport] 已连接到 {Endpoint}", _config.EventsEndpoint);
        }
        catch (Exception ex)
        {
            State = TransportState.Failed;
            _logger?.LogError(ex, "[SseTransport] 连接失败");
            throw;
        }
    }

    public Task DisconnectAsync(CancellationToken ct = default)
    {
        _disposeCts.Cancel();
        _sseListenTask = null;
        State = TransportState.Disconnected;
        _logger?.LogInformation("[SseTransport] 已断开");
        return Task.CompletedTask;
    }

    public async Task SendMessageAsync(string message, CancellationToken ct = default)
    {
        if (State != TransportState.Connected)
            throw new InvalidOperationException($"[SSE004] 传输未连接，当前状态: {State}");

        using var request = new HttpRequestMessage(HttpMethod.Post, _config.MessagesEndpoint);
        request.Content = new StringContent(message, Encoding.UTF8, "application/json");
        if (_config.AuthToken is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.AuthToken);
        }

        var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        OnMessage?.Invoke(this, new TransportMessageEventArgs
        {
            Message = message,
            Channel = TransportChannel.Output
        });
    }

    public async Task<string> WaitForOutputAsync(Func<string, bool> predicate, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        timeout ??= TimeSpan.FromSeconds(30);
        var startTime = _clock.GetUtcNow();

        while (_clock.GetUtcNow() - startTime < timeout)
        {
            ct.ThrowIfCancellationRequested();

            if (await _outputChannel.TryPredicateAsync(predicate, ct).ConfigureAwait(false))
            {
                return await _outputChannel.GetAllAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            }

            await Task.Delay(50, ct).ConfigureAwait(false);
        }

        throw new TimeoutException($"[TRN018] 等待SSE输出超时 (>{timeout.Value.TotalSeconds}s)");
    }

    public async Task<string> WaitForErrorAsync(Func<string, bool> predicate, TimeSpan? timeout = null, CancellationToken ct = default)
    {
        timeout ??= TimeSpan.FromSeconds(30);
        var startTime = _clock.GetUtcNow();

        while (_clock.GetUtcNow() - startTime < timeout)
        {
            ct.ThrowIfCancellationRequested();

            if (await _errorChannel.TryPredicateAsync(predicate, ct).ConfigureAwait(false))
            {
                return await _errorChannel.GetAllAsync(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            }

            await Task.Delay(50, ct).ConfigureAwait(false);
        }

        throw new TimeoutException($"[TRN019] 等待SSE错误超时 (>{timeout.Value.TotalSeconds}s)");
    }

    public Task<string> GetOutputAsync() =>
        _outputChannel.GetAllAsync(TimeSpan.FromSeconds(5));

    public Task<string> GetOutputIncrementalAsync() =>
        _outputChannel.GetIncrementalAsync(TimeSpan.FromSeconds(5));

    public Task<string> GetErrorAsync() =>
        _errorChannel.GetAllAsync(TimeSpan.FromSeconds(5));

    public Task<string> GetErrorIncrementalAsync() =>
        _errorChannel.GetIncrementalAsync(TimeSpan.FromSeconds(5));

    public Task ClearOutputAsync() =>
        _outputChannel.ClearAsync(TimeSpan.FromSeconds(5));

    public async ValueTask DisposeAsync()
    {
        _disposeCts.Cancel();
        _httpClient.Dispose();
        _outputChannel.Dispose();
        _errorChannel.Dispose();
        _disposeCts.Dispose();
        State = TransportState.Disconnected;
        await ValueTask.CompletedTask;
    }

    private Task StartSseListenerAsync(CancellationToken ct)
    {
        _sseListenTask = Task.Run(() => SseListenLoopAsync(ct), ct);
        return Task.CompletedTask;
    }

    private async Task SseListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && _reconnectAttempts < _config.MaxReconnectAttempts)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, _config.EventsEndpoint);
                request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
                if (_config.AuthToken is not null)
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _config.AuthToken);
                }

                using var response = await _httpClient.SendAsync(
                    request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode();

                _reconnectAttempts = 0;

                using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

                await ParseSseStreamAsync(stream, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _reconnectAttempts++;
                _logger?.LogWarning(ex, "[SseTransport] SSE 连接断开，重连 {Attempt}/{Max}",
                    _reconnectAttempts, _config.MaxReconnectAttempts);

                if (_reconnectAttempts >= _config.MaxReconnectAttempts)
                {
                    State = TransportState.Failed;
                    break;
                }

                var backoff = new ExponentialBackoff(
                    TimeSpan.FromMilliseconds(_config.ReconnectBackoffBaseMs),
                    TimeSpan.FromMilliseconds(_config.ReconnectBackoffBaseMs * 30));
                await Task.Delay(backoff.CalculateDelay(_reconnectAttempts - 1), ct).ConfigureAwait(false);
            }
        }
    }

    private async Task ParseSseStreamAsync(Stream stream, CancellationToken ct)
    {
        await foreach (var sseEvent in SseStreamParser.ParseAsync(stream, ct).ConfigureAwait(false))
        {
            var data = sseEvent.Data;
            var channel = sseEvent.EventType == "error" ? TransportChannel.Error : TransportChannel.Output;
            var buffered = channel == TransportChannel.Error ? _errorChannel : _outputChannel;

            await buffered.AddAsync(data, ct).ConfigureAwait(false);

            OnMessage?.Invoke(this, new TransportMessageEventArgs
            {
                Message = data,
                Channel = channel
            });
        }
    }
}
