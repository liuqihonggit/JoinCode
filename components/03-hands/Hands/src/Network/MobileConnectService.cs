using JoinCode.Abstractions.Attributes;

namespace IO.Services;

[Register]
public sealed partial class MobileConnectService : IMobileConnectService
{
    private System.Net.Sockets.TcpListener? _tcpListener;
    [Inject] private readonly ILogger<MobileConnectService>? _logger;
    private int _runningPort;
    private CancellationTokenSource? _cts;

    public MobileConnectService(ILogger<MobileConnectService>? logger = null)
    {
        _logger = logger;
    }

    public bool IsServerRunning => _tcpListener != null;

    public string GenerateConnectUrl(int port)
    {
        var host = System.Net.Dns.GetHostName();
        var p = port > 0 ? port : _runningPort;
        return $"http://{host}:{p}/connect";
    }

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
                System.Diagnostics.Trace.WriteLine($"MobileConnectService: failed to stop TCP listener: {ex.Message}");
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
                var client = await _tcpListener.AcceptTcpClientAsync(ct).ConfigureAwait(false);
                _ = HandleClientAsync(client, ct);
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
        }
    }

    private static async Task HandleClientAsync(System.Net.Sockets.TcpClient client, CancellationToken ct)
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
            System.Diagnostics.Trace.WriteLine($"MobileConnectService: client handling failed: {ex.Message}");
        }
        finally
        {
            client.Close();
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
}
