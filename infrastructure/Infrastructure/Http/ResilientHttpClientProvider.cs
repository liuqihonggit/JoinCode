namespace Infrastructure.Http;

public sealed class ResilientHttpClientProvider : IHttpClientProvider
{
    private readonly IHttpClientProvider _inner;
    private readonly ResiliencePolicy _policy;
    private readonly ILogger? _logger;
    private readonly ResilientHttpExecutor _executor;

    public ResilientHttpClientProvider(
        IHttpClientProvider inner,
        ResiliencePolicy? policy = null,
        ILogger? logger = null)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _policy = policy ?? ResiliencePolicy.HttpDefault("default");
        _logger = logger;
        _executor = new ResilientHttpExecutor(_policy, logger);
    }

    public ResilientHttpExecutor Executor => _executor;

    public HttpClient GetClient()
    {
        return _inner.GetClient();
    }

    public HttpClient GetClient(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        return _inner.GetClient(name);
    }

    public async Task<HttpResponseMessage> SendResilientAsync(
        HttpRequestMessage request,
        string operationName,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        return await _executor.ExecuteAsync(
            async token =>
            {
                var clone = await CloneRequestAsync(request).ConfigureAwait(false);
                return await _inner.GetClient().SendAsync(clone, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
            },
            operationName,
            ct).ConfigureAwait(false);
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        if (request.Content is not null)
        {
            var contentBytes = await request.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
            clone.Content = new ByteArrayContent(contentBytes);

            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        clone.Version = request.Version;

        return clone;
    }
}
