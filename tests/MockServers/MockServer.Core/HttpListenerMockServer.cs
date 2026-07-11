namespace MockServer.Core;

public sealed class HttpListenerMockServer : IHttpMockServer
{
    private readonly HttpListener _listener;
    private readonly CancellationTokenSource _cts;
    private readonly List<CapturedRequest> _capturedRequests = [];
    private readonly SemaphoreSlim _lock = new(1, 1);
    private Task? _listenTask;
    private int _requestIndex;
    private string _url = string.Empty;
    private readonly int _port;
    private readonly IResponseStrategy _responseStrategy;
    private readonly ICacheSimulator _cacheSimulator;
    private readonly ILogger? _logger;

    public string Url => _url;
    public MockServerStats Stats { get; } = new();
    public event Action? ShutdownRequested;

    public HttpListenerMockServer(
        IResponseStrategy responseStrategy,
        ICacheSimulator cacheSimulator,
        int port = 0,
        ILogger? logger = null)
    {
        ArgumentNullException.ThrowIfNull(responseStrategy);
        ArgumentNullException.ThrowIfNull(cacheSimulator);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

        _responseStrategy = responseStrategy;
        _cacheSimulator = cacheSimulator;
        _cts = new CancellationTokenSource();
        _listener = new HttpListener();
        _logger = logger;

        _port = port == 0 ? GetAvailablePort() : port;
        _listener.Prefixes.Add($"http://localhost:{_port}/");
    }

    private static int GetAvailablePort()
    {
        using var tcpListener = new TcpListener(IPAddress.Loopback, 0);
        tcpListener.Start();
        var port = ((IPEndPoint)tcpListener.LocalEndpoint).Port;
        tcpListener.Stop();
        return port;
    }

    public Task StartAsync(int port = 0)
    {
        try
        {
            _listener.Start();
        }
        catch (HttpListenerException ex)
        {
            throw new InvalidOperationException(
                $"HttpListener.Start() failed on port {_port}. " +
                $"This may be a .NET runtime compatibility issue. " +
                $"Error: {ex.Message}", ex);
        }
        _url = $"http://localhost:{_port}/";
        _listenTask = ListenLoop(_cts.Token);
        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        _cts.Cancel();
        _listener.Stop();
        return Task.CompletedTask;
    }

    private async Task ListenLoop(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                if (ct.IsCancellationRequested) break;

                var path = ctx.Request.Url?.AbsolutePath ?? "";

                if (ctx.Request.HttpMethod == "GET" && path == "/shutdown")
                {
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "application/json";
                    var shutdownBytes = Encoding.UTF8.GetBytes("{\"status\":\"shutting_down\"}");
                    await ctx.Response.OutputStream.WriteAsync(shutdownBytes, ct).ConfigureAwait(false);
                    ctx.Response.Close();
                    ShutdownRequested?.Invoke();
                    break;
                }

                if (ctx.Request.HttpMethod == "GET")
                {
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "application/json";
                    var healthBytes = Encoding.UTF8.GetBytes("{\"status\":\"ok\"}");
                    await ctx.Response.OutputStream.WriteAsync(healthBytes, ct).ConfigureAwait(false);
                    ctx.Response.Close();
                    continue;
                }

                using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
                var body = await reader.ReadToEndAsync(ct).ConfigureAwait(false);

                var captured = new CapturedRequest
                {
                    Method = ctx.Request.HttpMethod,
                    Path = path,
                    Body = body,
                    Headers = ctx.Request.Headers.AllKeys
                        .Where(k => k is not null)
                        .ToDictionary(k => k!, k => ctx.Request.Headers[k!] ?? ""),
                    Index = Interlocked.Increment(ref _requestIndex) - 1
                };

                var requestJson = JsonDocument.Parse(body);
                var cacheStats = _cacheSimulator.ComputeCacheStats(requestJson.RootElement);

                await _lock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    _capturedRequests.Add(captured);
                    Stats.TotalRequests++;

                    // 合并到同一把锁内，避免双重锁死锁
                    if (cacheStats.CacheReadTokens > 0) Stats.CacheHits++;
                    else Stats.CacheMisses++;
                }
                finally
                {
                    _lock.Release();
                }

                // 检查策略是否要求返回错误状态码
                var statusCode = _responseStrategy.GetHttpStatusCode(requestJson.RootElement);
                if (statusCode != 200)
                {
                    ctx.Response.StatusCode = statusCode;
                    ctx.Response.ContentType = "application/json";
                    var errorBody = _responseStrategy.BuildResponse(requestJson.RootElement, cacheStats);
                    var errorBytes = Encoding.UTF8.GetBytes(errorBody);
                    await ctx.Response.OutputStream.WriteAsync(errorBytes, ct).ConfigureAwait(false);
                    ctx.Response.Close();
                    continue;
                }

                // 检查是否为流式请求
                var isStream = requestJson.RootElement.TryGetProperty("stream", out var streamProp)
                    && streamProp.ValueKind == JsonValueKind.True;

                if (isStream && _responseStrategy.SupportsStreaming)
                {
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "text/event-stream";
                    ctx.Response.SendChunked = true;

                    var id = $"chatcmpl-{Guid.NewGuid():N}";

                    // 发送前导事件（Anthropic 需要 message_start + content_block_start）
                    var preamble = _responseStrategy.BuildStreamPreamble(id);
                    if (!string.IsNullOrEmpty(preamble))
                    {
                        var preambleBytes = Encoding.UTF8.GetBytes(preamble);
                        await ctx.Response.OutputStream.WriteAsync(preambleBytes, ct).ConfigureAwait(false);
                        await ctx.Response.OutputStream.FlushAsync(ct).ConfigureAwait(false);
                    }

                    var words = _responseStrategy.GetContentChunks();
                    foreach (var word in words)
                    {
                        var chunk = _responseStrategy.BuildStreamChunk(id, word, false);
                        var chunkBytes = Encoding.UTF8.GetBytes(chunk);
                        await ctx.Response.OutputStream.WriteAsync(chunkBytes, ct).ConfigureAwait(false);
                        await ctx.Response.OutputStream.FlushAsync(ct).ConfigureAwait(false);
                        await Task.Delay(20, ct).ConfigureAwait(false);
                    }

                    // 发送结束 chunk
                    var lastChunk = _responseStrategy.BuildStreamChunk(id, "", true);
                    var lastBytes = Encoding.UTF8.GetBytes(lastChunk);
                    await ctx.Response.OutputStream.WriteAsync(lastBytes, ct).ConfigureAwait(false);
                    ctx.Response.Close();
                }
                else
                {
                    var responseBody = _responseStrategy.BuildResponse(requestJson.RootElement, cacheStats);

                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "application/json";
                    var responseBytes = Encoding.UTF8.GetBytes(responseBody);
                    await ctx.Response.OutputStream.WriteAsync(responseBytes, ct).ConfigureAwait(false);
                    ctx.Response.Close();
                }
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested) { break; }
            catch (ObjectDisposedException) { break; }
        }
    }

    public CapturedRequest GetRequest(int index)
    {
        if (!_lock.Wait(5000))
            throw new TimeoutException("获取请求超时：锁被 ListenLoop 持有");
        try { return _capturedRequests[index]; }
        finally { _lock.Release(); }
    }

    public IReadOnlyList<CapturedRequest> GetAllRequests()
    {
        if (!_lock.Wait(5000))
            throw new TimeoutException("获取请求列表超时：锁被 ListenLoop 持有");
        try { return _capturedRequests.ToList(); }
        finally { _lock.Release(); }
    }

    public void Clear()
    {
        if (!_lock.Wait(5000))
            throw new TimeoutException("清除请求超时：锁被 ListenLoop 持有");
        try
        {
            _capturedRequests.Clear();
            _requestIndex = 0;
            Stats.TotalRequests = 0;
            Stats.CacheHits = 0;
            Stats.CacheMisses = 0;
        }
        finally
        {
            _lock.Release();
        }
        _cacheSimulator.ResetCache();
    }

    public async ValueTask DisposeAsync()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Listener stop failed: {ex.Message}"); }
        try { _listener.Close(); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Listener close failed: {ex.Message}"); }

        if (_listenTask is not null)
        {
            try { 
                await _listenTask.ConfigureAwait(false); 
            } 
            catch (Exception ex) { 
                System.Diagnostics.Trace.WriteLine($"Listen task failed during disposal: {ex.Message}"); 
                }
        }

        _cts.Dispose();
        _lock.Dispose();
    }
}
