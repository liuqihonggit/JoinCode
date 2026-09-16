namespace IO.Services;

/// <summary>移动端连接服务 — 在本机开启 TCP 监听，接受移动端发起的连接握手并返回连接确认响应。</summary>
[Register(typeof(IMobileConnectService), ServiceLifetime.Singleton)]
public sealed partial class MobileConnectService : ServiceEntity, IMobileConnectService, IDisposable
{
    private System.Net.Sockets.TcpListener? _tcpListener;
    private readonly ILogger<MobileConnectService>? _logger;
    private int _runningPort;
    private CancellationTokenSource? _cts;

    /// <summary>构造移动端连接服务实例。</summary>
    /// <param name="logger">可选的日志记录器，传入 null 时静默运行。</param>
    public MobileConnectService(ILogger<MobileConnectService>? logger = null)
    {
        _logger = logger;
    }

    /// <summary>获取一个值，指示连接服务是否正在监听端口。</summary>
    public bool IsServerRunning => _tcpListener != null;

    /// <summary>基于本机主机名和指定端口生成移动端可访问的连接 URL。</summary>
    /// <param name="port">连接端口，小于等于 0 时使用当前已启动的监听端口。</param>
    /// <returns>形如 <c>http://{host}:{port}/connect</c> 的连接 URL。</returns>
    public string GenerateConnectUrl(int port)
    {
        var host = System.Net.Dns.GetHostName();
        var p = port > 0 ? port : _runningPort;
        return $"http://{host}:{p}/connect";
    }

    /// <summary>异步启动连接服务，自动选取可用端口开始监听并进入接受循环。</summary>
    /// <param name="ct">可取消令牌，用于取消启动过程。</param>
    /// <returns>实际监听的端口号。</returns>
    public Task<int> StartConnectServerAsync(CancellationToken ct = default)
    {
        var port = FindAvailablePort();
        _runningPort = port;

        _tcpListener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Any, port);
        _cts = new CancellationTokenSource();

        try
        {
            _tcpListener.Start();
            _logger?.LogInformation("移动端连接服务已启动，端口: {Port}", port);
            _ = AcceptLoopAsync(_cts.Token);
        }
        catch (System.Net.Sockets.SocketException ex)
        {
            _logger?.LogError(ex, "启动移动端连接服务失败");
            _tcpListener = null;
        }

        return Task.FromResult(port);
    }

    /// <summary>停止连接服务，取消监听并关闭 TCP 监听器。</summary>
    public void StopConnectServer()
    {
        _cts?.Cancel();

        if (_tcpListener != null)
        {
            try
            {
                _tcpListener.Stop();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "MobileConnectService: 停止 TCP 监听器失败");
            }
            _logger?.LogInformation("移动端连接服务已停止");
        }
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        if (_tcpListener == null) return;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var client = await _tcpListener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                await HandleClientAsync(client, ct, _logger).ConfigureAwait(false);
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (System.Net.Sockets.SocketException)
            {
                break;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            // P1-10: 兜底捕获所有未预期异常，避免 fire-and-forget 触发 UnobservedTaskException
            catch (Exception ex)
            {
                _logger?.LogError(ex, "移动端连接服务 AcceptLoop 未预期异常");
                break;
            }
        }
    }

    private static async Task HandleClientAsync(System.Net.Sockets.TcpClient client, CancellationToken ct, ILogger<MobileConnectService>? logger = null)
    {
        try
        {
            using var stream = client.GetStream();
            var buffer = new byte[4096];
            var bytesRead = await stream.ReadAsync(buffer, ct).ConfigureAwait(false);

            var response = "HTTP/1.1 200 OK\r\nContent-Type: application/json\r\nConnection: close\r\n\r\n{\"status\":\"connected\",\"version\":\"1.0\"}";
            var responseBytes = System.Text.Encoding.UTF8.GetBytes(response);
            await stream.WriteAsync(responseBytes, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "MobileConnectService: 客户端处理失败");
        }
    }

    private static int FindAvailablePort()
    {
        using var listener = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        listener.Start();
        var port = ((System.Net.IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    /// <summary>释放移动端连接服务资源 — P1-10: 补全 IDisposable 避免资源累积</summary>
    public override void Dispose()
    {
        StopConnectServer();
        _cts?.Dispose();
        _cts = null;
        _tcpListener = null;
            base.Dispose();
    }
}
