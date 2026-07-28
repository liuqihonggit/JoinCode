
namespace Services.Api.Vcr;

public sealed partial class VcrHttpHandler : DelegatingHandler
{
    private readonly IVcrService _vcrService;
    private readonly VcrOptions _options;
    [Inject] private readonly ILogger<VcrHttpHandler>? _logger;
    private string _currentCassetteName = string.Empty;

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

    public void SetCassette(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        _currentCassetteName = name;
        _logger?.LogDebug("VCR cassette 设置为: {Name}", name);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var vcrRequest = await ConvertToVcrRequestAsync(request).ConfigureAwait(false);

        if (_vcrService.CurrentMode == VcrMode.Playback && !string.IsNullOrEmpty(_currentCassetteName))
        {
            var recordedResponse = await _vcrService.FindMatchingInteractionAsync(
                _currentCassetteName, vcrRequest, cancellationToken).ConfigureAwait(false);

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
                _currentCassetteName, vcrRequest, vcrResponse, cancellationToken).ConfigureAwait(false);
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
