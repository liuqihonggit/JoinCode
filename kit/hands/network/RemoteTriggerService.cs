namespace IO.Services;

/// <summary>远程触发器服务 — 通过 HTTP 调用 JCC API 端点，执行触发器的列表、获取、创建、更新与运行操作。</summary>
[Register(typeof(IRemoteTriggerService), ServiceLifetime.Singleton)]
public sealed partial class RemoteTriggerService : ServiceEntity, IRemoteTriggerService
{
    private readonly HttpClient _httpClient;
    private readonly IConfigurationService? _configService;
    private readonly ILogger<RemoteTriggerService>? _logger;

    /// <summary>构造远程触发器服务实例。</summary>
    /// <param name="httpClient">用于发送 HTTP 请求的客户端。</param>
    /// <param name="configService">可选的配置服务，用于读取 API 端点与认证令牌。</param>
    /// <param name="logger">可选的日志记录器，传入 null 时静默运行。</param>
    public RemoteTriggerService(HttpClient httpClient, IConfigurationService? configService = null, ILogger<RemoteTriggerService>? logger = null)
    {
        _httpClient = httpClient;
        _configService = configService;
        _logger = logger;
    }

    /// <summary>异步执行远程触发器操作，依据动作类型构造请求并附带认证令牌与超时控制。</summary>
    /// <param name="action">要执行的触发器动作类型。</param>
    /// <param name="triggerId">触发器标识，获取、更新、运行动作时必需。</param>
    /// <param name="body">请求体 JSON，创建与更新动作时使用。</param>
    /// <param name="ct">可取消令牌。</param>
    /// <returns>包含状态码与响应体的触发器结果。</returns>
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
            using var cts = TimeoutHelper.CreateLinkedTimeout(ct, TimeSpan.FromSeconds(20));

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
            catch (Exception ex) { _logger?.LogWarning(ex, "RemoteTriggerService: 从配置获取端点失败"); }
        }

        return null;
    }

    private async Task<string?> GetAuthTokenAsync(CancellationToken ct)
    {
        if (_configService != null)
        {
            try
            {
                return await _configService.GetAsync("api.key", ct).ConfigureAwait(false);
            }
            catch (Exception ex) { _logger?.LogWarning(ex, "RemoteTriggerService: 从配置获取认证令牌失败"); }
        }

        return null;
    }
}
