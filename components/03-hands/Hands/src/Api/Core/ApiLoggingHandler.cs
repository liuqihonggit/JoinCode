
namespace Services.Api;

/// <summary>
/// API 日志处理器配置
/// </summary>
public sealed record ApiLoggingOptions
{
    /// <summary>
    /// 是否记录请求体
    /// </summary>
    public bool LogRequestBody { get; init; } = false;

    /// <summary>
    /// 是否记录响应体
    /// </summary>
    public bool LogResponseBody { get; init; } = false;

    /// <summary>
    /// 请求体最大记录长度
    /// </summary>
    public int MaxRequestBodyLength { get; init; } = 1000;

    /// <summary>
    /// 响应体最大记录长度
    /// </summary>
    public int MaxResponseBodyLength { get; init; } = 1000;

    /// <summary>
    /// 是否记录请求头
    /// </summary>
    public bool LogRequestHeaders { get; init; } = false;

    /// <summary>
    /// 是否记录响应头
    /// </summary>
    public bool LogResponseHeaders { get; init; } = false;

    /// <summary>
    /// 敏感头名称列表（将被遮盖）
    /// </summary>
    public IReadOnlySet<string> SensitiveHeaders { get; init; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "Authorization",
        "X-Api-Key",
        "X-Auth-Token",
        "Cookie",
        "Set-Cookie"
    };

    /// <summary>
    /// 创建默认配置
    /// </summary>
    public static ApiLoggingOptions Default => new();

    /// <summary>
    /// 创建详细日志配置
    /// </summary>
    public static ApiLoggingOptions Verbose => new()
    {
        LogRequestBody = true,
        LogResponseBody = true,
        LogRequestHeaders = true,
        LogResponseHeaders = true,
        MaxRequestBodyLength = 5000,
        MaxResponseBodyLength = 5000
    };

    /// <summary>
    /// 创建仅记录错误配置
    /// </summary>
    public static ApiLoggingOptions ErrorsOnly => new()
    {
        LogRequestBody = false,
        LogResponseBody = false,
        LogRequestHeaders = false,
        LogResponseHeaders = false
    };
}

/// <summary>
/// API 请求/响应日志处理器
/// </summary>
public sealed partial class ApiLoggingHandler : DelegatingHandler
{
    [Inject] private readonly ILogger<ApiLoggingHandler> _logger;
    private readonly ApiLoggingOptions _options;

    public ApiLoggingHandler(
        ILogger<ApiLoggingHandler> logger,
        ApiLoggingOptions? options = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options ?? ApiLoggingOptions.Default;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        var requestId = Guid.NewGuid().ToString("N")[..8];

        await LogRequestAsync(request, requestId).ConfigureAwait(false);

        HttpResponseMessage? response = null;
        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
            return response;
        }
        finally
        {
            stopwatch.Stop();
            await LogResponseAsync(response, requestId, stopwatch.Elapsed, request).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 记录请求信息
    /// </summary>
    private async Task LogRequestAsync(HttpRequestMessage request, string requestId, CancellationToken cancellationToken = default)
    {
        try
        {
            var method = request.Method;
            var uri = request.RequestUri?.ToString() ?? "unknown";

            var logMessage = $"[API] [{requestId}] 请求: {method} {uri}";

            if (_options.LogRequestHeaders && request.Headers.Any())
            {
                var headers = FormatHeaders(request.Headers);
                logMessage += $"\n  Headers: {headers}";
            }

            if (_options.LogRequestBody && request.Content != null)
            {
                var body = await ReadContentAsync(request.Content, _options.MaxRequestBodyLength).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(body))
                {
                    logMessage += $"\n  Body: {body}";
                }
            }

            _logger.LogDebug(logMessage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[API] [{RequestId}] 记录请求信息失败", requestId);
        }
    }

    /// <summary>
    /// 记录响应信息
    /// </summary>
    private async Task LogResponseAsync(
        HttpResponseMessage? response,
        string requestId,
        TimeSpan duration,
        HttpRequestMessage request,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (response == null)
            {
                _logger.LogWarning("[API] [{RequestId}] 响应为空", requestId);
                return;
            }

            var method = request.Method;
            var uri = request.RequestUri?.ToString() ?? "unknown";
            var statusCode = (int)response.StatusCode;
            var statusText = response.StatusCode.ToString();

            var logLevel = statusCode >= 400 ? LogLevel.Warning : LogLevel.Debug;
            var logMessage = $"[API] [{requestId}] 响应: {method} {uri} -> {statusCode} {statusText} ({duration.TotalMilliseconds:F1}ms)";

            if (_options.LogResponseHeaders && response.Headers.Any())
            {
                var headers = FormatHeaders(response.Headers);
                logMessage += $"\n  Headers: {headers}";
            }

            if (_options.LogResponseBody)
            {
                var body = await ReadContentAsync(response.Content, _options.MaxResponseBodyLength).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(body))
                {
                    logMessage += $"\n  Body: {body}";
                }
            }

            _logger.Log(logLevel, logMessage);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[API] [{RequestId}] 记录响应信息失败", requestId);
        }
    }

    /// <summary>
    /// 格式化请求头
    /// </summary>
    private string FormatHeaders(IEnumerable<KeyValuePair<string, IEnumerable<string>>> headers)
    {
        var formattedHeaders = headers
            .Select(h =>
            {
                var key = h.Key;
                var value = _options.SensitiveHeaders.Contains(key)
                    ? "***"
                    : string.Join(", ", h.Value);
                return $"{key}: {value}";
            });

        return "{ " + string.Join(", ", formattedHeaders) + " }";
    }

    /// <summary>
    /// 读取内容（不消耗原始内容）
    /// </summary>
    private static async Task<string?> ReadContentAsync(HttpContent? content, int maxLength)
    {
        if (content == null)
        {
            return null;
        }

        try
        {
            var stream = await content.ReadAsStreamAsync().ConfigureAwait(false);
            await using var _ = stream.ConfigureAwait(false);

            if (!stream.CanSeek)
            {
                return null;
            }

            var originalPosition = stream.Position;

            using var reader = new StreamReader(stream, leaveOpen: true);
            var buffer = new char[maxLength];
            var readLength = await reader.ReadAsync(buffer, 0, maxLength).ConfigureAwait(false);
            var result = new string(buffer, 0, readLength);

            if (readLength == maxLength)
            {
                var peekBuffer = new char[1];
                var peeked = await reader.ReadAsync(peekBuffer, 0, 1).ConfigureAwait(false);
                if (peeked > 0)
                {
                    result += "... (truncated)";
                }
            }

            stream.Position = originalPosition;
            return result;
        }
        catch
        {
            return null;
        }
    }
}
