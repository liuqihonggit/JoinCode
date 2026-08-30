namespace Core.Security.Sandbox.Ipc;


public sealed class SandboxIpcClient : IAsyncDisposable
{
    private readonly IProcessService _processService;
    private readonly IFileSystem _fs;
    private readonly ILogger<SandboxIpcClient>? _logger;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly Func<int, Task>? _onSatelliteStarted;
    private IInteractiveProcess? _process;
    private int _requestCounter;
    private readonly ConcurrentDictionary<string, TaskCompletionSource<SandboxIpcResponse>> _pendingRequests = new();
    private readonly AsyncLock _startLock = new();
    private Task? _readLoopTask;
    private CancellationTokenSource? _readCts;

    public SandboxIpcClient(IProcessService processService, IFileSystem fs, ILogger<SandboxIpcClient>? logger = null, Func<int, Task>? onSatelliteStarted = null)
    {
        _processService = processService;
        _fs = fs;
        _logger = logger;
        _onSatelliteStarted = onSatelliteStarted;
    }

    public bool IsRunning => _process is not null && !_process.HasExited;

    public int? SatelliteProcessId => _process is not null && !_process.HasExited ? _process.Id : null;

    public async Task StartAsync(string? satelliteExePath = null, CancellationToken ct = default)
    {
        using (await _startLock.LockAsync(ct).ConfigureAwait(false))
        {
            if (_process is not null && !_process.HasExited)
            {
                return;
            }

            var exePath = satelliteExePath ?? DiscoverSatelliteExe();

            _readCts = new CancellationTokenSource();
            _process = await _processService.StartInteractiveAsync(new InteractiveProcessOptions
            {
                FileName = exePath,
                Arguments = "",
            }, ct).ConfigureAwait(false);

            _readLoopTask = ReadLoopAsync(_readCts.Token);

            if (_onSatelliteStarted is not null)
            {
                try
                {
                    await _onSatelliteStarted(_process.Id).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "[SandboxIpcClient] 卫星进程 PID 回调执行失败, Pid: {Pid}", _process.Id);
                }
            }

            _logger?.LogInformation("[SandboxIpcClient] 卫星进程已启动: {ExePath}, Pid: {Pid}", exePath, _process.Id);
        }
    }

    public async Task<SandboxExecuteResponse> ExecuteAsync(SandboxExecuteRequest request, CancellationToken ct = default)
    {
        EnsureRunning();

        var requestId = Interlocked.Increment(ref _requestCounter).ToString();
        var requestJson = JsonSerializer.Serialize(request, SandboxIpcJsonContext.Default.SandboxExecuteRequest);

        var ipcRequest = new SandboxIpcRequest
        {
            Type = "execute",
            RequestId = requestId,
            Payload = requestJson
        };

        var response = await SendRequestAsync(ipcRequest, ct).ConfigureAwait(false);

        if (!response.Success)
        {
            throw new InvalidOperationException($"Sandbox execute failed: {response.Error}");
        }

        return JsonSerializer.Deserialize(response.Payload ?? "", SandboxIpcJsonContext.Default.SandboxExecuteResponse)
            ?? throw new InvalidOperationException("Failed to parse execute response");
    }

    public async Task PingAsync(CancellationToken ct = default)
    {
        EnsureRunning();

        var requestId = Interlocked.Increment(ref _requestCounter).ToString();
        var request = new SandboxIpcRequest
        {
            Type = "ping",
            RequestId = requestId
        };

        var response = await SendRequestAsync(request, ct).ConfigureAwait(false);

        if (!response.Success || response.Type != "pong")
        {
            throw new InvalidOperationException($"Ping failed: {response.Error}");
        }
    }

    public async Task ShutdownAsync(CancellationToken ct = default)
    {
        if (_process is null || _process.HasExited)
        {
            return;
        }

        var requestId = Interlocked.Increment(ref _requestCounter).ToString();
        var request = new SandboxIpcRequest
        {
            Type = "shutdown",
            RequestId = requestId
        };

        try
        {
            await SendRequestAsync(request, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "[SandboxIpcClient] shutdown 请求发送异常，忽略");
        }

        _readCts?.Cancel();

        if (_process is not null && !_process.HasExited)
        {
            _process.Kill();
        }
    }

    private async Task<SandboxIpcResponse> SendRequestAsync(SandboxIpcRequest request, CancellationToken ct)
    {
        var tcs = new TaskCompletionSource<SandboxIpcResponse>();
        _pendingRequests[request.RequestId] = tcs;

        try
        {
            var json = JsonSerializer.Serialize(request, SandboxIpcJsonContext.Default.SandboxIpcRequest);

            await _sendLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await _process!.StandardInput.WriteAsync((json + "\n").AsMemory(), ct).ConfigureAwait(false);
                await _process.StandardInput.FlushAsync(ct).ConfigureAwait(false);
            }
            finally
            {
                _sendLock.Release();
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));

            try
            {
                return await tcs.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested)
            {
                throw new TimeoutException("[GRD002] IPC 请求超时 (60s)，卫星进程未响应");
            }
        }
        finally
        {
            _pendingRequests.TryRemove(request.RequestId, out _);
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _process is not null && !_process.HasExited)
            {
                var line = await _process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                try
                {
                    var response = JsonSerializer.Deserialize(line, SandboxIpcJsonContext.Default.SandboxIpcResponse);
                    if (response is not null && _pendingRequests.TryRemove(response.RequestId, out var tcs))
                    {
                        tcs.SetResult(response);
                    }
                }
                catch (Exception ex)
                {
                    _logger?.LogWarning(ex, "[SandboxIpcClient] 解析响应失败: {Line}", line);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[SandboxIpcClient] 读取循环异常");
        }
    }

    private void EnsureRunning()
    {
        if (_process is null || _process.HasExited)
        {
            throw new InvalidOperationException("[GRD003] 卫星进程未运行，请先调用 StartAsync");
        }
    }

    private string DiscoverSatelliteExe()
    {
        var currentDir = AppContext.BaseDirectory;
        var exeName = OperatingSystem.IsWindows() ? "jcc-sandbox.exe" : "jcc-sandbox";

        var path = Path.Combine(currentDir, exeName);
        if (_fs.FileExists(path))
        {
            return path;
        }

        path = Path.Combine(currentDir, "tools", exeName);
        if (_fs.FileExists(path))
        {
            return path;
        }

        throw new FileNotFoundException($"[GRD011] 找不到沙箱卫星程序: {exeName}");
    }

    public async ValueTask DisposeAsync()
    {
        await ShutdownAsync().ConfigureAwait(false);
        _sendLock.Dispose();
        _startLock.Dispose();
        _readCts?.Dispose();
    }
}
