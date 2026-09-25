namespace Core.Security.Sandbox.Ipc;


/// <summary>
/// 沙箱 IPC 客户端 — 通过 stdin/stdout 与沙箱卫星进程进行 JSON 行协议通信,转发执行请求并等待响应
/// </summary>
public sealed class SandboxIpcClient : IAsyncDisposable {
    private readonly IProcessService _processService;
    private readonly IFileSystem _fs;
    private readonly ILogger<SandboxIpcClient>? _logger;
    private readonly Func<int, Task>? _onSatelliteStarted;
    private IInteractiveProcess? _process;
    private int _requestCounter;
    private volatile ImmutableDictionary<string, TaskCompletionSource<SandboxIpcResponse>> _pendingRequests = ImmutableDictionary<string, TaskCompletionSource<SandboxIpcResponse>>.Empty;
    private readonly AsyncLock _startLock = new();
    private Task? _readLoopTask;
    private CancellationTokenSource? _readCts;
    private Channel<string>? _writeChannel;
    private CancellationTokenSource? _writeCts;
    private Task? _writeConsumerTask;
    private int _disposed;

    /// <summary>
    /// 初始化沙箱 IPC 客户端实例
    /// </summary>
    public SandboxIpcClient(IProcessService processService, IFileSystem fs, ILogger<SandboxIpcClient>? logger = null, Func<int, Task>? onSatelliteStarted = null) {
        _processService = processService;
        _fs = fs;
        _logger = logger;
        _onSatelliteStarted = onSatelliteStarted;
    }

    /// <summary>
    /// 卫星进程是否正在运行
    /// </summary>
    public bool IsRunning => _process is not null && !_process.HasExited;

    /// <summary>
    /// 卫星进程 ID;若未运行则返回 null
    /// </summary>
    public int? SatelliteProcessId => _process is not null && !_process.HasExited ? _process.Id : null;

    /// <summary>
    /// 启动沙箱卫星进程并建立读写循环
    /// </summary>
    public async Task StartAsync(string? satelliteExePath = null, CancellationToken ct = default) {
        using (await _startLock.TryLockAsync(ct).ConfigureAwait(false) ?? throw new System.TimeoutException($"锁 '{_startLock.Name}' 等待超时")) {
            if (_process is not null && !_process.HasExited) {
                return;
            }

            var exePath = satelliteExePath ?? DiscoverSatelliteExe();

            _readCts = new CancellationTokenSource();
            _process = await _processService.StartInteractiveAsync(new InteractiveProcessOptions {
                FileName = exePath,
                Arguments = "",
            }, ct).ConfigureAwait(false);

            _readLoopTask = ReadLoopAsync(_readCts.Token);

            _writeChannel = Channel.CreateBounded<string>(new BoundedChannelOptions(256) { FullMode = BoundedChannelFullMode.Wait });
            _writeCts = new CancellationTokenSource();
            _writeConsumerTask = WriteLoopAsync(_writeCts.Token);

            if (_onSatelliteStarted is not null) {
                try {
                    await _onSatelliteStarted(_process.Id).ConfigureAwait(false);
                } catch (Exception ex) {
                    _logger?.LogWarning(ex, "[SandboxIpcClient] 卫星进程 PID 回调执行失败, Pid: {Pid}", _process.Id);
                }
            }

            _logger?.LogInformation("[SandboxIpcClient] 卫星进程已启动: {ExePath}, Pid: {Pid}", exePath, _process.Id);
        }
    }

    /// <summary>
    /// 通过 IPC 向卫星进程发送执行请求并等待响应
    /// </summary>
    public async Task<SandboxExecuteResponse> ExecuteAsync(SandboxExecuteRequest request, CancellationToken ct = default) {
        EnsureRunning();

        var requestId = Interlocked.Increment(ref _requestCounter).ToString();
        var requestJson = JsonSerializer.Serialize(request, SandboxIpcJsonContext.Default.SandboxExecuteRequest);

        var ipcRequest = new SandboxIpcRequest {
            Type = "execute",
            RequestId = requestId,
            Payload = requestJson
        };

        var response = await SendRequestAsync(ipcRequest, ct).ConfigureAwait(false);

        if (!response.Success) {
            throw new InvalidOperationException($"Sandbox execute failed: {response.Error}");
        }

        return RelaxedJsonSerializer.Deserialize(response.Payload ?? "", SandboxIpcJsonContext.Default.SandboxExecuteResponse)
            ?? throw new InvalidOperationException("Failed to parse execute response");
    }

    /// <summary>
    /// 向卫星进程发送 ping 请求以检测连通性
    /// </summary>
    public async Task PingAsync(CancellationToken ct = default) {
        EnsureRunning();

        var requestId = Interlocked.Increment(ref _requestCounter).ToString();
        var request = new SandboxIpcRequest {
            Type = "ping",
            RequestId = requestId
        };

        var response = await SendRequestAsync(request, ct).ConfigureAwait(false);

        if (!response.Success || response.Type != "pong") {
            throw new InvalidOperationException($"Ping failed: {response.Error}");
        }
    }

    /// <summary>
    /// 向卫星进程发送 shutdown 请求并关闭读写通道与进程
    /// </summary>
    public async Task ShutdownAsync(CancellationToken ct = default) {
        if (_process is null || _process.HasExited) {
            return;
        }

        var requestId = Interlocked.Increment(ref _requestCounter).ToString();
        var request = new SandboxIpcRequest {
            Type = "shutdown",
            RequestId = requestId
        };

        try {
            await SendRequestAsync(request, ct).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger?.LogDebug(ex, "[SandboxIpcClient] shutdown 请求发送异常，忽略");
        }

        _writeCts?.Cancel();
        _writeChannel?.Writer.TryComplete();

        if (_writeConsumerTask != null) {
            try {
                await _writeConsumerTask.ConfigureAwait(false);
            } catch (Exception ex) {
                _logger?.LogDebug(ex, "[SandboxIpcClient] 等待写消费者完成时出错");
            }
        }

        _readCts?.Cancel();

        if (_process is not null && !_process.HasExited) {
            _process.Kill();
        }
    }

    private async Task<SandboxIpcResponse> SendRequestAsync(SandboxIpcRequest request, CancellationToken ct) {
        var tcs = new TaskCompletionSource<SandboxIpcResponse>();
        SetPendingRequest(request.RequestId, tcs);

        try {
            var json = JsonSerializer.Serialize(request, SandboxIpcJsonContext.Default.SandboxIpcRequest);

            await _writeChannel!.Writer.WriteAsync(json + "\n", ct).ConfigureAwait(false);


            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(60));

            try {
                return await tcs.Task.WaitAsync(timeoutCts.Token).ConfigureAwait(false);
            } catch (OperationCanceledException) when (!ct.IsCancellationRequested) {
                throw new TimeoutException("[GRD002] IPC 请求超时 (60s)，卫星进程未响应");
            }
        } finally {
            TryRemovePendingRequest(request.RequestId);
        }
    }

    /// <summary>
    /// 写消费者循环 — 单消费者从 Channel 串行写 stdin,消除持锁 await IO 死锁风险(P9)
    /// </summary>
    private async Task WriteLoopAsync(CancellationToken cancellationToken) {
        try {
            if (_writeChannel is null) return;
            await foreach (var json in _writeChannel.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false)) {
                if (_process is null || _process.HasExited) break;
                await _process.StandardInput.WriteAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
                await _process.StandardInput.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
        } catch (OperationCanceledException) { } catch (ChannelClosedException) { _logger?.LogDebug("[SandboxIpcClient] 写通道已关闭"); } catch (Exception ex) {
            _logger?.LogError(ex, "[SandboxIpcClient] 写循环异常");
        }
    }

    private async Task ReadLoopAsync(CancellationToken ct) {
        try {
            while (!ct.IsCancellationRequested && _process is not null && !_process.HasExited) {
                var line = await _process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
                if (line is null) {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line)) {
                    continue;
                }

                try {
                    var response = RelaxedJsonSerializer.Deserialize(line, SandboxIpcJsonContext.Default.SandboxIpcResponse);
                    if (response is not null && TryRemovePendingRequest(response.RequestId, out var tcs)) {
                        tcs.SetResult(response);
                    }
                } catch (Exception ex) {
                    _logger?.LogWarning(ex, "[SandboxIpcClient] 解析响应失败: {Line}", line);
                }
            }
        } catch (OperationCanceledException) {
        } catch (Exception ex) {
            _logger?.LogError(ex, "[SandboxIpcClient] 读取循环异常");
        }
    }

    private void EnsureRunning() {
        if (_process is null || _process.HasExited) {
            throw new InvalidOperationException("[GRD003] 卫星进程未运行，请先调用 StartAsync");
        }
    }

    private string DiscoverSatelliteExe() {
        var currentDir = AppContext.BaseDirectory;
        var exeName = OperatingSystem.IsWindows() ? "jcc-sandbox.exe" : "jcc-sandbox";

        var path = Path.Combine(currentDir, exeName);
        if (_fs.FileExists(path)) {
            return path;
        }

        path = Path.Combine(currentDir, "tools", exeName);
        if (_fs.FileExists(path)) {
            return path;
        }

        throw new FileNotFoundException($"[GRD011] 找不到沙箱卫星程序: {exeName}");
    }

    /// <summary>
    /// 异步释放客户端,关闭卫星进程并释放所有资源
    /// </summary>
    public ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return ValueTask.CompletedTask;
        return new ValueTask(ShutdownAsync().ContinueWith(
            static (_, state) => {
                var self = (SandboxIpcClient)state!;
                self._startLock.Dispose();
                self._readCts?.Dispose();
                self._writeCts?.Dispose();
            },
            this,
            TaskContinuationOptions.ExecuteSynchronously));
    }

    private void SetPendingRequest(string requestId, TaskCompletionSource<SandboxIpcResponse> tcs) {
        var current = _pendingRequests;
        while (true) {
            var updated = current.SetItem(requestId, tcs);
            if (Interlocked.CompareExchange(ref _pendingRequests, updated, current) == current) return;
            current = _pendingRequests;
        }
    }

    private bool TryRemovePendingRequest(string requestId, out TaskCompletionSource<SandboxIpcResponse> tcs) {
        tcs = null!;
        var current = _pendingRequests;
        while (current.ContainsKey(requestId)) {
            tcs = current[requestId];
            var updated = current.Remove(requestId);
            if (Interlocked.CompareExchange(ref _pendingRequests, updated, current) == current) return true;
            current = _pendingRequests;
        }
        return false;
    }

    private void TryRemovePendingRequest(string requestId) {
        var current = _pendingRequests;
        while (current.ContainsKey(requestId)) {
            var updated = current.Remove(requestId);
            if (Interlocked.CompareExchange(ref _pendingRequests, updated, current) == current) return;
            current = _pendingRequests;
        }
    }
}