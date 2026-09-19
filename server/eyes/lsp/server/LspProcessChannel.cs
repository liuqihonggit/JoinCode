namespace Services.Lsp;

/// <summary>
/// LSP 进程通道 — 封装进程生命周期、stdin/stdout 传输、读取循环
/// 从 LspClient 提取,所有进程IO集中于此,通过回调通知消息
/// </summary>
internal sealed class LspProcessChannel : IAsyncDisposable {
    private readonly IFileSystem _fs;
    private readonly IProcessService _processService;
    private readonly ILogger? _logger;
    private IInteractiveProcess? _process;
    private CancellationTokenSource? _readCts;
    private int _disposed;

    /// <summary>是否已连接到 LSP 服务器</summary>
    public bool IsConnected => _process != null && !_process.HasExited;

    /// <summary>读取取消令牌 — 供外部启动读取循环</summary>
    public CancellationToken ReadToken => _readCts?.Token ?? CancellationToken.None;

    /// <summary>
    /// 构造 LspProcessChannel
    /// </summary>
    public LspProcessChannel(IFileSystem fs, IProcessService processService, ILogger? logger = null) {
        _fs = fs ?? throw new ArgumentNullException(nameof(fs));
        _processService = processService ?? throw new ArgumentNullException(nameof(processService));
        _logger = logger;
    }

    /// <summary>获取工作区根目录 — 回退到当前目录</summary>
    public string GetRootDir(string? workingDirectory) => workingDirectory ?? _fs.GetCurrentDirectory();

    /// <summary>
    /// 启动 LSP 服务器进程 — 对齐 TS 端 connect
    /// </summary>
    public async Task StartAsync(LspServerConfig config, CancellationToken cancellationToken) {
        var options = new InteractiveProcessOptions {
            FileName = config.Command,
            ArgumentList = config.Arguments,
            WorkingDirectory = config.WorkingDirectory,
        };

        _process = await _processService.StartInteractiveAsync(options, cancellationToken).ConfigureAwait(false);
        _readCts = new CancellationTokenSource();

        _process.ErrorDataReceived += (_, line) => {
            if (line != null) _logger?.LogDebug("LSP stderr: {Line}", line);
        };
    }

    /// <summary>
    /// 发送 JSON-RPC 消息 — Content-Length 头 + body
    /// </summary>
    public async Task SendMessageAsync(string json, CancellationToken cancellationToken = default) {
        if (_process == null) return;

        var bytes = Encoding.UTF8.GetBytes(json);
        var header = $"Content-Length: {bytes.Length}\r\n\r\n";
        var headerBytes = Encoding.UTF8.GetBytes(header);

        var stream = _process.StandardInput.BaseStream;
        await stream.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);
        await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 读取循环 — 解析 Content-Length 头 + 读取 body,通过回调通知每条消息
    /// </summary>
    public async Task ReadLoopAsync(Func<string, CancellationToken, Task> onMessage, CancellationToken cancellationToken) {
        try {
            while (!cancellationToken.IsCancellationRequested && _process != null) {
                var reader = _process.StandardOutput;
                var headerLine = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (headerLine == null) break;

                if (!headerLine.StartsWith("Content-Length: "))
                    continue;

                var contentLength = int.Parse(headerLine["Content-Length: ".Length..]);

                await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);

                var buffer = new char[contentLength];
                var read = 0;
                while (read < contentLength) {
                    var n = await reader.ReadAsync(buffer, read, contentLength - read).ConfigureAwait(false);
                    if (n == 0) break;
                    read += n;
                }

                var json = new string(buffer);
                await onMessage(json, cancellationToken).ConfigureAwait(false);
            }
        } catch (OperationCanceledException) {
        } catch (Exception ex) {
            _logger?.LogError(ex, "LSP读取循环错误");
        }
    }

    /// <summary>
    /// 断开连接 — 取消读取 + 终止进程
    /// </summary>
    public async Task DisconnectAsync(Func<Task>? sendShutdownNotification = null, CancellationToken cancellationToken = default) {
        _readCts?.Cancel();

        if (_process != null && !_process.HasExited) {
            try {
                if (sendShutdownNotification is not null) {
                    await sendShutdownNotification().ConfigureAwait(false);
                }
                _process.Kill();
            } catch (Exception ex) { _logger?.LogWarning(ex, "LSP 客户端关闭通知发送失败"); }
        }

        if (_process is not null) await _process.DisposeAsync().ConfigureAwait(false);
        _process = null;
    }

    /// <summary>异步释放 — 断开连接</summary>
    public async ValueTask DisposeAsync() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        await DisconnectAsync().ConfigureAwait(false);
    }
}