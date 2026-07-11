namespace MockServer.Core;

public sealed class KestrelMockServer : IHttpMockServer
{
    private readonly IResponseStrategy _responseStrategy;
    private readonly ICacheSimulator _cacheSimulator;
    private readonly int _port;
    private readonly ILogger? _logger;
    private readonly string _serverName;
    private readonly List<CapturedRequest> _capturedRequests = [];
    private readonly SemaphoreSlim _lock = new(1, 1);
    private int _requestIndex;
    private WebApplication? _app;
    private Task? _runTask;
    private CancellationTokenSource _cts = new();
    private IHostApplicationLifetime? _appLifetime;
    private string _dumpDir = string.Empty;

    public string Url { get; private set; } = string.Empty;
    public MockServerStats Stats { get; } = new();
    public event Action? ShutdownRequested;

    public KestrelMockServer(
        IResponseStrategy responseStrategy,
        ICacheSimulator cacheSimulator,
        int port = 0,
        ILogger? logger = null,
        string serverName = "MockServer")
    {
        ArgumentNullException.ThrowIfNull(responseStrategy);
        ArgumentNullException.ThrowIfNull(cacheSimulator);
        ArgumentOutOfRangeException.ThrowIfLessThan(port, 0);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(port, 65535);

        _responseStrategy = responseStrategy;
        _cacheSimulator = cacheSimulator;
        _port = port == 0 ? GetAvailablePort() : port;
        _logger = logger;
        _serverName = serverName;
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
        _dumpDir = Path.Combine(Environment.CurrentDirectory, "tests", "MockServers", "MockServer.Core", "dumps", _serverName);
        Directory.CreateDirectory(_dumpDir);

        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls($"http://localhost:{_port}/");
        builder.Logging.ClearProviders();
        if (_logger is not null)
        {
            builder.Services.AddSingleton(_logger);
        }

        _app = builder.Build();
        _appLifetime = _app.Services.GetRequiredService<IHostApplicationLifetime>();

        _app.MapGet("/shutdown", async (HttpContext ctx) =>
        {
            Console.WriteLine($"[{_serverName}] Shutdown requested from {ctx.Connection.RemoteIpAddress}");
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"status\":\"shutting_down\"}");
            ShutdownRequested?.Invoke();
            // 触发 ASP.NET Core 优雅关闭：IHostApplicationLifetime.StopApplication()
            // 会让 _app.RunAsync() 任务完成，否则进程无法退出
            _appLifetime?.StopApplication();
        });

        _app.MapGet("/", async (HttpContext ctx) =>
        {
            ctx.Response.ContentType = "application/json";
            await ctx.Response.WriteAsync("{\"status\":\"ok\"}");
        });

        _app.MapPost("{**path}", async (HttpContext ctx) =>
        {
            using var reader = new StreamReader(ctx.Request.Body, Encoding.UTF8);
            var body = await reader.ReadToEndAsync(ctx.RequestAborted);

            var path = ctx.Request.Path.Value ?? "";
            var requestIndex = Interlocked.Increment(ref _requestIndex) - 1;
            var captured = new CapturedRequest
            {
                Method = ctx.Request.Method,
                Path = path,
                Body = body,
                Headers = ctx.Request.Headers.ToDictionary(h => h.Key, h => h.Value.ToString()),
                Index = requestIndex
            };

            Console.WriteLine($"[{_serverName}] === Request #{requestIndex} ===");
            Console.WriteLine($"[{_serverName}]   Path: {path}");
            Console.WriteLine($"[{_serverName}]   Client: {ctx.Connection.RemoteIpAddress}:{ctx.Connection.RemotePort}");
            Console.WriteLine($"[{_serverName}]   Body Length: {body.Length} chars");

            var requestJson = JsonDocument.Parse(body);
            var cacheStats = _cacheSimulator.ComputeCacheStats(requestJson.RootElement);

            Console.WriteLine($"[{_serverName}]   Cache: {(cacheStats.CacheReadTokens > 0 ? "HIT" : "MISS")} (creation={cacheStats.CacheCreationTokens}, read={cacheStats.CacheReadTokens}, input={cacheStats.InputTokens})");

            var messagesPreview = ExtractMessagesPreview(requestJson.RootElement);
            if (messagesPreview.Count > 0)
            {
                Console.WriteLine($"[{_serverName}]   Messages:");
                foreach (var line in messagesPreview)
                {
                    Console.WriteLine($"[{_serverName}]     {line}");
                }
            }

            DumpConversationToFile(requestIndex, body, cacheStats);

            await _lock.WaitAsync(ctx.RequestAborted);
            try
            {
                _capturedRequests.Add(captured);
                Stats.TotalRequests++;
                if (cacheStats.CacheReadTokens > 0) Stats.CacheHits++;
                else Stats.CacheMisses++;
            }
            finally
            {
                _lock.Release();
            }

            _responseStrategy.OnRequestStarted(requestJson.RootElement);

            var statusCode = _responseStrategy.GetHttpStatusCode(requestJson.RootElement);
            if (statusCode != 200)
            {
                ctx.Response.StatusCode = statusCode;
                ctx.Response.ContentType = "application/json";
                var errorBody = _responseStrategy.BuildResponse(requestJson.RootElement, cacheStats);
                await ctx.Response.WriteAsync(errorBody, ctx.RequestAborted);
                return;
            }

            var isStream = requestJson.RootElement.TryGetProperty("stream", out var streamProp)
                && streamProp.ValueKind == JsonValueKind.True;

            if (isStream && _responseStrategy.SupportsStreaming)
            {
                ctx.Response.StatusCode = 200;
                ctx.Response.ContentType = "text/event-stream";

                var id = $"chatcmpl-{Guid.NewGuid():N}";

                var preamble = _responseStrategy.BuildStreamPreamble(id);
                if (!string.IsNullOrEmpty(preamble))
                {
                    await ctx.Response.WriteAsync(preamble, ctx.RequestAborted);
                    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
                }

                if (_responseStrategy.HasThinkingContent())
                {
                    Console.WriteLine($"[{_serverName}]   Response: thinking content stream");
                    var thinkingStream = _responseStrategy.BuildStreamThinkingResponse(id);
                    await ctx.Response.WriteAsync(thinkingStream, ctx.RequestAborted);
                    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
                }

                if (_responseStrategy.HasToolCalls())
                {
                    Console.WriteLine($"[{_serverName}]   Response: tool call stream");
                    var toolCallStream = _responseStrategy.BuildStreamToolCallResponse(id);
                    await ctx.Response.WriteAsync(toolCallStream, ctx.RequestAborted);
                    await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
                }
                else
                {
                    var words = _responseStrategy.GetContentChunks();
                    Console.WriteLine($"[{_serverName}]   Response: text stream ({words.Length} chunks)");
                    foreach (var word in words)
                    {
                        var chunk = _responseStrategy.BuildStreamChunk(id, word, false);
                        await ctx.Response.WriteAsync(chunk, ctx.RequestAborted);
                        await ctx.Response.Body.FlushAsync(ctx.RequestAborted);
                        await Task.Delay(20, ctx.RequestAborted);
                    }

                    // 最终 chunk 包含 usage/cache stats — 真实 LLM API 在最后一个 chunk 返回 usage
                    var lastChunk = _responseStrategy.BuildStreamFinalChunk(id, cacheStats);
                    await ctx.Response.WriteAsync(lastChunk, ctx.RequestAborted);
                }
            }
            else
            {
                if (_responseStrategy.HasToolCalls())
                {
                    Console.WriteLine($"[{_serverName}]   Response: tool call (non-stream)");
                    var toolCallBody = _responseStrategy.BuildToolCallResponse(requestJson.RootElement, cacheStats);
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync(toolCallBody, ctx.RequestAborted);
                }
                else
                {
                    Console.WriteLine($"[{_serverName}]   Response: text (non-stream)");
                    var responseBody = _responseStrategy.BuildResponse(requestJson.RootElement, cacheStats);
                    ctx.Response.StatusCode = 200;
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.WriteAsync(responseBody, ctx.RequestAborted);
                }
            }

            Console.WriteLine($"[{_serverName}] === Request #{requestIndex} complete ===");
        });

        Url = $"http://localhost:{_port}/";
        _runTask = _app.RunAsync();

        Console.WriteLine($"[{_serverName}] ========================================");
        Console.WriteLine($"[{_serverName}]   Server:    {_serverName}");
        Console.WriteLine($"[{_serverName}]   URL:       {Url}");
        Console.WriteLine($"[{_serverName}]   Port:      {_port}");
        Console.WriteLine($"[{_serverName}]   Dump Dir:  {_dumpDir}");
        Console.WriteLine($"[{_serverName}] ========================================");

        return Task.CompletedTask;
    }

    public Task StopAsync()
    {
        // 优先触发 ASP.NET Core 优雅关闭；同时取消 _cts 以唤醒其它等待者
        _appLifetime?.StopApplication();
        _cts.Cancel();
        return Task.CompletedTask;
    }

    public CapturedRequest GetRequest(int index)
    {
        if (!_lock.Wait(5000))
            throw new TimeoutException("获取请求超时：锁被持有");
        try { return _capturedRequests[index]; }
        finally { _lock.Release(); }
    }

    public IReadOnlyList<CapturedRequest> GetAllRequests()
    {
        if (!_lock.Wait(5000))
            throw new TimeoutException("获取请求列表超时：锁被持有");
        try { return _capturedRequests.ToList(); }
        finally { _lock.Release(); }
    }

    public void Clear()
    {
        if (!_lock.Wait(5000))
            throw new TimeoutException("清除请求超时：锁被持有");
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

        if (_app is not null)
        {
            try { await _app.DisposeAsync(); } catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"App disposal failed: {ex.Message}"); }
        }

        if (_runTask is not null)
        {
            try { await _runTask; } catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"Run task failed during disposal: {ex.Message}"); }
        }

        _cts.Dispose();
        _lock.Dispose();
    }

    private void DumpConversationToFile(int requestIndex, string body, CacheStats cacheStats)
    {
        try
        {
            var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            var fileName = $"req_{requestIndex:D4}_{timestamp}.txt";
            var filePath = Path.Combine(_dumpDir, fileName);

            var sb = new StringBuilder();
            sb.AppendLine($"# {_serverName} Request #{requestIndex}");
            sb.AppendLine($"Timestamp: {DateTime.UtcNow:O}");
            sb.AppendLine($"Cache: {(cacheStats.CacheReadTokens > 0 ? "HIT" : "MISS")}");
            sb.AppendLine($"CacheCreationTokens: {cacheStats.CacheCreationTokens}");
            sb.AppendLine($"CacheReadTokens: {cacheStats.CacheReadTokens}");
            sb.AppendLine($"InputTokens: {cacheStats.InputTokens}");
            sb.AppendLine($"OutputTokens: {cacheStats.OutputTokens}");
            sb.AppendLine();

            var requestJson = JsonDocument.Parse(body);
            sb.AppendLine("## System Prefix");
            var prefix = TokenEstimator.ExtractSystemPrefix(requestJson.RootElement);
            sb.AppendLine(prefix.Length > 200 ? prefix[..200] + "..." : prefix);
            sb.AppendLine($"(prefix length: {prefix.Length} chars)");
            sb.AppendLine();

            sb.AppendLine("## Messages");
            if (requestJson.RootElement.TryGetProperty("messages", out var messages))
            {
                foreach (var msg in messages.EnumerateArray())
                {
                    var role = msg.TryGetProperty("role", out var r) ? r.GetString() ?? "?" : "?";
                    var content = msg.TryGetProperty("content", out var c)
                        ? c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : c.GetRawText()
                        : "";
                    var preview = content.Length > 300 ? content[..300] + "..." : content;
                    sb.AppendLine($"[{role}] {preview}");
                    sb.AppendLine($"  (content length: {content.Length} chars)");
                }
            }

            sb.AppendLine();
            sb.AppendLine("## Raw Request Body");
            sb.AppendLine(body);

            File.WriteAllText(filePath, sb.ToString());
            Console.WriteLine($"[{_serverName}]   Dumped: {filePath}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{_serverName}]   Dump failed: {ex.Message}");
        }
    }

    private static List<string> ExtractMessagesPreview(JsonElement request)
    {
        var lines = new List<string>();
        if (!request.TryGetProperty("messages", out var messages)) return lines;

        foreach (var msg in messages.EnumerateArray())
        {
            var role = msg.TryGetProperty("role", out var r) ? r.GetString() ?? "?" : "?";
            var content = msg.TryGetProperty("content", out var c)
                ? c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : c.GetRawText()
                : "";
            var preview = content.Length > 100 ? content[..100] + "..." : content;
            lines.Add($"[{role}] {preview} ({content.Length} chars)");
        }

        return lines;
    }
}
