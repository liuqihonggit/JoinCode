namespace JoinCode.Transport;

/// <summary>
/// 基于命名管道的 HTTP 消息处理器
/// </summary>
public sealed class PipeHttpMessageHandler : HttpMessageHandler
{
    private readonly string _pipeName;
    private readonly ILogger? _logger;
    private const int ConnectTimeoutMs = 5000;
    private const int ReadBufferSize = 8192;

    /// <summary>
    /// 创建管道 HTTP 消息处理器
    /// </summary>
    /// <param name="pipeName">管道名称</param>
    /// <param name="logger">日志记录器（可选）</param>
    public PipeHttpMessageHandler(string pipeName, ILogger? logger = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(pipeName);

        _pipeName = pipeName;
        _logger = logger;
    }

    /// <summary>
    /// 发送 HTTP 请求
    /// </summary>
    /// <param name="request">HTTP 请求</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>HTTP 响应</returns>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        _logger?.LogDebug("{Method} Sending HTTP request via pipe: {PipeName}", nameof(SendAsync), _pipeName);

        using var pipeClient = new NamedPipeClientStream(
            ".",
            _pipeName,
            PipeDirection.InOut,
            PipeOptions.Asynchronous);

        try
        {
            // 连接到管道服务器
            await pipeClient.ConnectAsync(ConnectTimeoutMs, cancellationToken);
            _logger?.LogDebug("Connected to pipe: {PipeName}", _pipeName);

            // 序列化请求
            var requestText = await HttpRequestSerializer.SerializeAsync(request, cancellationToken);
            var requestBytes = Encoding.UTF8.GetBytes(requestText);

            // 发送请求（包含长度前缀便于服务器解析）
            await pipeClient.WriteAsync(requestBytes, cancellationToken);
            await pipeClient.FlushAsync(cancellationToken);
            _logger?.LogDebug("Request sent, length: {Length} bytes", requestBytes.Length);

            // 读取响应
            var responseText = await ReadResponseAsync(pipeClient, cancellationToken);
            _logger?.LogDebug("Response received, length: {Length} bytes", responseText.Length);

            // 解析响应
            return HttpRequestSerializer.Deserialize(responseText);
        }
        catch (TimeoutException ex)
        {
            _logger?.LogError(ex, "Timeout connecting to pipe: {PipeName}", _pipeName);
            throw new HttpRequestException($"管道连接超时: {_pipeName}", ex);
        }
        catch (IOException ex)
        {
            _logger?.LogError(ex, "IO error communicating with pipe: {PipeName}", _pipeName);
            throw new HttpRequestException($"管道通信错误: {_pipeName}", ex);
        }
    }

    private static async Task<string> ReadResponseAsync(NamedPipeClientStream pipeClient, CancellationToken cancellationToken)
    {
        using var memoryStream = new MemoryStream();
        var buffer = new byte[ReadBufferSize];

        int bytesRead;
        while ((bytesRead = await pipeClient.ReadAsync(buffer, cancellationToken)) > 0)
        {
            await memoryStream.WriteAsync(buffer.AsMemory(0, bytesRead), cancellationToken);
        }

        return Encoding.UTF8.GetString(memoryStream.ToArray());
    }

    /// <summary>
    /// 释放资源
    /// </summary>
    /// <param name="disposing">是否释放托管资源</param>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _logger?.LogDebug("{Method} Disposing PipeHttpMessageHandler", nameof(Dispose));
        }

        base.Dispose(disposing);
    }
}
