
namespace Services.Api.Vcr;

/// <summary>
/// VCR HTTP 处理器，作为 DelegatingHandler 拦截 HTTP 请求实现录制/回放
/// </summary>
public sealed partial class VcrHttpHandler : DelegatingHandler
{
    private readonly IVcrService _vcrService;
    private readonly VcrOptions _options;
    private readonly ILogger<VcrHttpHandler>? _logger;
    private string _currentCassetteName = string.Empty;
    private string? _currentCassetteDirectory;

    /// <summary>
    /// 构造 VcrHttpHandler
    /// </summary>
    /// <param name="vcrService">VCR 服务实例</param>
    /// <param name="options">VCR 配置选项</param>
    /// <param name="logger">可选日志记录器</param>
    public VcrHttpHandler(
        IVcrService vcrService,
        VcrOptions options,
        ILogger<VcrHttpHandler>? logger = null)
    {
        ArgumentNullException.ThrowIfNull(vcrService);
        ArgumentNullException.ThrowIfNull(options);
        _vcrService = vcrService;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// 设置当前使用的 cassette 名称与目录
    /// </summary>
    /// <param name="name">cassette 名称</param>
    /// <param name="directory">可选目录覆盖</param>
    public void SetCassette(string name, string? directory = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        _currentCassetteName = name;
        _currentCassetteDirectory = directory;
        _logger?.LogDebug("VCR cassette 设置为: {Name} (目录: {Directory})", name, directory ?? "(默认)");
    }

    /// <summary>
    /// 拦截 HTTP 请求：回放模式下优先匹配录制响应，录制模式下持久化交互
    /// </summary>
    /// <param name="request">HTTP 请求消息</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>HTTP 响应消息</returns>
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var vcrRequest = await ConvertToVcrRequestAsync(request).ConfigureAwait(false);

        if (_vcrService.CurrentMode == VcrMode.Playback && !string.IsNullOrEmpty(_currentCassetteName))
        {
            var recordedResponse = await _vcrService.FindMatchingInteractionAsync(
                _currentCassetteName, vcrRequest, _currentCassetteDirectory, cancellationToken).ConfigureAwait(false);

            if (recordedResponse != null)
            {
                return ConvertToHttpResponseMessage(recordedResponse, request);
            }
        }

        HttpResponseMessage response;
        try
        {
            response = await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            if (_vcrService.CurrentMode == VcrMode.Playback && !string.IsNullOrEmpty(_currentCassetteName))
            {
                _logger?.LogWarning("请求失败且回放模式无匹配: {Method} {Uri}", vcrRequest.Method, vcrRequest.Uri);
            }
            throw;
        }

        if (_vcrService.CurrentMode == VcrMode.Record && !string.IsNullOrEmpty(_currentCassetteName))
        {
            var vcrResponse = await ConvertToVcrResponseAsync(response).ConfigureAwait(false);
            await _vcrService.RecordInteractionAsync(
                _currentCassetteName, vcrRequest, vcrResponse, _currentCassetteDirectory, cancellationToken).ConfigureAwait(false);
        }

        return response;
    }

    private static async Task<VcrRequest> ConvertToVcrRequestAsync(HttpRequestMessage request)
    {
        string? body = null;
        if (request.Content != null)
        {
            body = await request.Content.ReadAsStringAsync().ConfigureAwait(false);
        }

        var headers = new Dictionary<string, string>();
        foreach (var header in request.Headers)
        {
            headers[header.Key] = string.Join(", ", header.Value);
        }

        return new VcrRequest
        {
            Method = request.Method.Method,
            Uri = request.RequestUri?.ToString() ?? string.Empty,
            Headers = headers,
            Body = body
        };
    }

    private static async Task<VcrResponse> ConvertToVcrResponseAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        var headers = new Dictionary<string, string>();

        foreach (var header in response.Headers)
        {
            headers[header.Key] = string.Join(", ", header.Value);
        }

        foreach (var header in response.Content.Headers)
        {
            headers[header.Key] = string.Join(", ", header.Value);
        }

        return new VcrResponse
        {
            Status = (int)response.StatusCode,
            StatusText = response.ReasonPhrase ?? string.Empty,
            Headers = headers,
            Body = body,
            ContentType = response.Content.Headers.ContentType?.MediaType
        };
    }

    private static HttpResponseMessage ConvertToHttpResponseMessage(VcrResponse vcrResponse, HttpRequestMessage request)
    {
        var response = new HttpResponseMessage((HttpStatusCode)vcrResponse.Status)
        {
            ReasonPhrase = vcrResponse.StatusText,
            RequestMessage = request
        };

        if (vcrResponse.Body != null)
        {
            var mediaType = vcrResponse.ContentType ?? "application/json";
            response.Content = new StringContent(vcrResponse.Body, Encoding.UTF8, mediaType);
        }

        foreach (var header in vcrResponse.Headers)
        {
            if (!response.Headers.TryAddWithoutValidation(header.Key, header.Value))
            {
                response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return response;
    }
}
