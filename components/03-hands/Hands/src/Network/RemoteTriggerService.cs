namespace IO.Services;

[Register]
public sealed partial class RemoteTriggerService : IRemoteTriggerService
{
    private readonly HttpClient _httpClient;
    private readonly IConfigurationService? _configService;
    [Inject] private readonly ILogger<RemoteTriggerService>? _logger;

    public RemoteTriggerService(HttpClient httpClient, IConfigurationService? configService = null, ILogger<RemoteTriggerService>? logger = null)
    {
        _httpClient = httpClient;
        _configService = configService;
        _logger = logger;
    }

    public async Task<TriggerResult> ExecuteAsync(TriggerAction action, string? triggerId = null, string? body = null, CancellationToken ct = default)
    {
        var baseUrl = await GetApiBaseUrlAsync(ct).ConfigureAwait(false);
        if (string.IsNullOrEmpty(baseUrl))
        {
            return new TriggerResult { Status = 401, Json = """{"error":"未配置 JCC API 端点，请设置 JCC_ENDPOINT 环境变量"}""" };
        }

        var (method, url) = BuildRequest(action, baseUrl, triggerId);
        var request = new HttpRequestMessage(method, url);

        var token = await GetAuthTokenAsync(ct).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(token))
        {
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        }

        request.Headers.Add("anthropic-version", "2024-01-01");
        request.Headers.Add("anthropic-beta", "tengu-surreal-dali-2025-04-01");

        if (body != null && (method == HttpMethod.Post || method == HttpMethod.Put))
        {
            request.Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json");
        }

        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(20));

            var response = await _httpClient.SendAsync(request, cts.Token).ConfigureAwait(false);
            var responseBody = await response.Content.ReadAsStringAsync(cts.Token).ConfigureAwait(false);

            return new TriggerResult { Status = (int)response.StatusCode, Json = responseBody };
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "远程触发器 API 调用失败");
            return new TriggerResult { Status = 500, Json = $"{{\"error\":\"{ex.Message}\"}}" };
        }
    }

    private static (HttpMethod Method, string Url) BuildRequest(TriggerAction action, string baseUrl, string? triggerId)
    {
        return action switch
        {
            TriggerAction.List => (HttpMethod.Get, $"{baseUrl}/v1/code/triggers"),
            TriggerAction.Get => (HttpMethod.Get, $"{baseUrl}/v1/code/triggers/{triggerId}"),
            TriggerAction.Create => (HttpMethod.Post, $"{baseUrl}/v1/code/triggers"),
            TriggerAction.Update => (HttpMethod.Post, $"{baseUrl}/v1/code/triggers/{triggerId}"),
            TriggerAction.Run => (HttpMethod.Post, $"{baseUrl}/v1/code/triggers/{triggerId}/run"),
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
    }

    private async Task<string?> GetApiBaseUrlAsync(CancellationToken ct)
    {
        var envEndpoint = Environment.GetEnvironmentVariable(JccEnvVar.Endpoint.ToValue());
        if (!string.IsNullOrEmpty(envEndpoint)) return envEndpoint.TrimEnd('/');

        if (_configService != null)
        {
            try
            {
                var saved = await _configService.GetAsync("api.endpoint", ct).ConfigureAwait(false);
                if (!string.IsNullOrEmpty(saved)) return saved.TrimEnd('/');
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"RemoteTriggerService: failed to get endpoint from config: {ex.Message}"); }
        }

        return null;
    }

    private async Task<string?> GetAuthTokenAsync(CancellationToken ct)
    {
        var envKey = Environment.GetEnvironmentVariable(JccEnvVar.ApiKey.ToValue());
        if (!string.IsNullOrEmpty(envKey)) return envKey;

        if (_configService != null)
        {
            try
            {
                return await _configService.GetAsync("api.key", ct).ConfigureAwait(false);
            }
            catch (Exception ex) { System.Diagnostics.Trace.WriteLine($"RemoteTriggerService: failed to get auth token from config: {ex.Message}"); }
        }

        return null;
    }
}
