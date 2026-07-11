
namespace Core.Hooks.Execution;

/// <summary>
/// HTTP 钩子执行器
/// 发送 HTTP POST 请求到外部服务
/// </summary>
[Register(typeof(IHookExecutor))]
public sealed class HttpHookExecutor : HookExecutorBase<HttpHook>
{
    private readonly IHttpClientFactory? _httpClientFactory;
    [Inject] private readonly IClockService _clock;

    public HttpHookExecutor(
        IHttpClientFactory? httpClientFactory = null,
        ILogger<HttpHookExecutor>? logger = null,
        IClockService? clock = null)
        : base(logger)
    {
        _httpClientFactory = httpClientFactory;
        _clock = clock ?? SystemClockService.Instance;
    }

    /// <inheritdoc />
    public override string SupportedType => HookTypeConstants.Http;

    /// <inheritdoc />
    public override async Task<HookResult> ExecuteTypedAsync(
        HttpHook hook,
        HookInput input,
        CancellationToken cancellationToken = default)
    {
        LogExecutionStart(hook, input);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var context = CreateContext(hook, input);

            var result = await ExecuteWithTimeoutAsync(
                ct => ExecuteHttpRequestAsync(hook, input, ct),
                context.Timeout,
                hook.GetDisplayText(),
                cancellationToken).ConfigureAwait(false);

            LogExecutionComplete(hook, result, stopwatch.Elapsed);
            return result;
        }
        catch (HookTimeoutException)
        {
            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            Logger?.LogError(ex, "Failed to execute HTTP hook");
            return HookResult.NonBlockingError(
                error: ex.Message,
                message: $"HTTP hook failed: {ex.Message}");
        }
    }

    private async Task<HookResult> ExecuteHttpRequestAsync(
        HttpHook hook,
        HookInput input,
        CancellationToken cancellationToken)
    {
        // IHttpClientFactory 未注册时返回非阻塞错误
        if (_httpClientFactory is null)
        {
            return HookResult.NonBlockingError("IHttpClientFactory 未注册，HTTP 钩子执行器不可用");
        }

        var client = _httpClientFactory.CreateClient("HookHttpClient");

        // 构建请求
        var request = new HttpRequestMessage(HttpMethod.Post, hook.Url);

        // 添加请求头
        request.Headers.Add("X-Hook-Event", input.Event.ToEventName());
        request.Headers.Add("X-Hook-Tool-Name", input.ToolName ?? "");
        request.Headers.Add("X-Hook-Tool-Use-Id", input.ToolUseId ?? "");
        request.Headers.Add("X-Hook-Session-Id", input.SessionId ?? "");

        // 添加自定义请求头（支持环境变量插值）
        if (hook.Headers != null)
        {
            foreach (var header in hook.Headers)
            {
                var value = InterpolateEnvVars(header.Value, hook.AllowedEnvVars);
                request.Headers.TryAddWithoutValidation(header.Key, value);
            }
        }

        // 构建请求体
        var payload = new HookHttpPayload
        {
            Event = input.Event.ToEventName(),
            EventName = input.EventName,
            ToolName = input.ToolName,
            ToolUseId = input.ToolUseId,
            SessionId = input.SessionId,
            Matcher = input.Matcher,
            Payload = input.Payload,
            Timestamp = _clock.GetUtcNowOffset()
        };

        request.Content = JsonContent.Create(payload, jsonTypeInfo: HooksJsonContext.Default.HookHttpPayload);

        // 发送请求
        var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);

        // 读取响应
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

        // 解析响应
        return ParseHttpResponse(response, responseBody, hook.Url);
    }

    private HookResult ParseHttpResponse(HttpResponseMessage response, string body, string url)
    {
        // HTTP 200-299 - 成功
        if (response.IsSuccessStatusCode)
        {
            // 尝试解析 JSON 响应
            if (!string.IsNullOrEmpty(body) && body.Trim().StartsWith('{'))
            {
                try
                {
                    return ParseJsonResponse(body);
                }
                catch (Exception ex)
                {
                    Logger?.LogWarning(ex, "Failed to parse HTTP hook JSON response");
                }
            }

            return HookResult.Success(message: $"HTTP {response.StatusCode}: {body}");
        }

        // HTTP 403 - 阻塞
        if (response.StatusCode == System.Net.HttpStatusCode.Forbidden)
        {
            return HookResult.Blocking(
                error: body ?? "Blocked by external service",
                command: url,
                message: body);
        }

        // 其他错误 - 非阻塞
        return HookResult.NonBlockingError(
            error: $"HTTP {response.StatusCode}: {body}",
            message: $"External service returned {response.StatusCode}");
    }

    private HookResult ParseJsonResponse(string json)
    {
        var hookDecision = JsonSerializer.Deserialize(json, HooksJsonContext.Default.HookDecision);
        if (hookDecision is null)
            return new HookResult { Outcome = HookOutcome.Success };

        var outcome = HookOutcome.Success;
        var preventContinuation = false;
        string? message = null;

        if (hookDecision.Decision?.ToLowerInvariant() == PermissionBehaviorConstants.Block)
        {
            outcome = HookOutcome.Blocking;
            preventContinuation = true;
        }

        message = hookDecision.Reason ?? hookDecision.Message;

        if (hookDecision.Continue.HasValue)
            preventContinuation = !hookDecision.Continue.Value;

        return new HookResult
        {
            Outcome = outcome,
            Message = message,
            PreventContinuation = preventContinuation
        };
    }

    private string InterpolateEnvVars(string value, IReadOnlyList<string>? allowedEnvVars)
    {
        if (string.IsNullOrEmpty(value) || allowedEnvVars == null || !allowedEnvVars.Any())
        {
            return value;
        }

        var result = new StringBuilder(value);

        foreach (var varName in allowedEnvVars)
        {
            var placeholder = $"${{{varName}}}";
            var envValue = Environment.GetEnvironmentVariable(varName);

            if (envValue != null)
            {
                result.Replace(placeholder, envValue);
            }
        }

        return result.ToString();
    }
}

/// <summary>
/// HTTP 钩子请求体
/// </summary>
public sealed record HookHttpPayload
{
    public required string Event { get; init; }
    public required string EventName { get; init; }
    public string? ToolName { get; init; }
    public string? ToolUseId { get; init; }
    public string? SessionId { get; init; }
    public string? Matcher { get; init; }
    public Dictionary<string, JsonElement> Payload { get; init; } = new();
    public DateTimeOffset Timestamp { get; init; }
}
