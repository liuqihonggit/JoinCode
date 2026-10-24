namespace JoinCode.Gui.WebHost;

/// <summary>
/// 模型发现（TASK066 C8 <c>llm/discoverModels</c>）：按供应商协议拉 <c>/v1/models</c>，
/// 把各协议不同的响应形状归一成 DSH 的 <c>{id, name?, contextWindow?, maxTokens?, inputModalities?}</c>。
/// 供应商响应字段是动态的（OpenAI 系 <c>context_length</c>、Anthropic 系 <c>display_name</c>），
/// 因此这里用 <see cref="JsonDocument"/> 只做字段过滤（ADR 0133 允许的动态过滤场景），不手写 JSON 拼接。
/// </summary>
internal sealed class DshModelDiscoveryService {
    private readonly DshCredentialStore _credentials;

    /// <summary>构造发现服务</summary>
    /// <param name="credentials">凭据存储（请求未带密钥时按供应商路由复用已存密钥）</param>
    internal DshModelDiscoveryService(DshCredentialStore credentials) => _credentials = credentials;

    /// <summary>HTTP 处理器工厂（默认真实网络；测试注入记录型处理器）</summary>
    internal Func<HttpMessageHandler> HandlerFactory { get; set; } = () => new HttpClientHandler();

    /// <summary>一次发现最多回传的模型数（防止供应商返回超长清单拖垮前端下拉）</summary>
    private const int ModelLimit = 500;

    /// <summary>请求超时（发现是用户主动点按的一次性动作，超时后给出可重试的引导错误）</summary>
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(20);

    /// <summary>
    /// 执行一次模型发现；失败返回 <c>llm/discoverModels-rejected</c> 并解释原因与下一步。
    /// </summary>
    /// <param name="request">发现请求（provider/baseURL/api/apiKey 均可省略，按已存配置补全）</param>
    /// <param name="cancellationToken">取消令牌</param>
    internal async Task<DshRpcResult> DiscoverAsync(DshModelDiscoveryRequest request, CancellationToken cancellationToken) {
        if (NormalizeBaseUrl(request.BaseUrl) is not { } baseUrl) {
            return DshRpcResults.Fail("llm/discoverModels-rejected",
                $"llm/discoverModels 需要 baseURL（供应商 API 基地址，如 https://api.deepseek.com/v1）；收到 baseURL={(request.BaseUrl is null ? "(缺失)" : request.BaseUrl)}");
        }
        var apiKey = await ResolveApiKeyAsync(request, cancellationToken);
        if (apiKey is null) {
            return DshRpcResults.Fail("llm/discoverModels-rejected",
                $"缺少 API Key：请先在模型页为供应商 {DescribeProvider(request)} 保存密钥，或在发现请求的 apiKey 字段直接填写");
        }
        using var client = new HttpClient(HandlerFactory(), disposeHandler: true) { Timeout = RequestTimeout };
        using var message = BuildRequest(ModelsEndpoint(baseUrl), request.Api, apiKey);
        try {
            using var response = await client.SendAsync(message, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return response.IsSuccessStatusCode
                ? DshRpcResults.Ok(JsonSerializer.SerializeToElement(ParseModels(body), DshJsonContext.Default.ListDshDiscoveredModel))
                : DshRpcResults.Fail("llm/discoverModels-rejected",
                    $"供应商拒绝模型列表请求：HTTP {(int)response.StatusCode}（{response.ReasonPhrase}），URL={message.RequestUri}；请核对 baseURL 与 API Key。响应片段：{Truncate(body)}");
        } catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested) {
            return DshRpcResults.Fail("llm/discoverModels-rejected",
                $"模型发现超时（{RequestTimeout.TotalSeconds:0} 秒），URL={message.RequestUri}；请检查网络或改用可直连的 baseURL 后重试");
        } catch (HttpRequestException ex) {
            return DshRpcResults.Fail("llm/discoverModels-rejected",
                $"模型发现请求失败：{ex.Message}，URL={message.RequestUri}；请检查 baseURL 是否可达（含代理设置）");
        }
    }

    /// <summary>解析密钥：请求显式提供的优先，其次按供应商路由读环境变量/共享 auth.json</summary>
    private async Task<string?> ResolveApiKeyAsync(DshModelDiscoveryRequest request, CancellationToken cancellationToken) {
        if (!string.IsNullOrWhiteSpace(request.ApiKey)) {
            return request.ApiKey.Trim();
        }
        if (string.IsNullOrWhiteSpace(request.Provider)) {
            return null;
        }
        return await _credentials.FindAsync(DshCredentialStore.DeriveKeyRef(request.Provider), cancellationToken);
    }

    /// <summary>基地址归一：去尾部斜杠；空/非 http 视为缺失</summary>
    private static string? NormalizeBaseUrl(string? baseUrl) {
        if (string.IsNullOrWhiteSpace(baseUrl)) {
            return null;
        }
        var trimmed = baseUrl.Trim().TrimEnd('/');
        return Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" ? trimmed : null;
    }

    /// <summary>模型列表端点：基地址已含版本段（/v1、/v1beta 等）时直接拼 /models，否则补 /v1/models</summary>
    internal static string ModelsEndpoint(string baseUrl) {
        var lastSegment = baseUrl[(baseUrl.LastIndexOf('/') + 1)..];
        return lastSegment.Length > 1 && lastSegment[0] == 'v' && lastSegment[1..].All(char.IsAsciiDigit)
            ? $"{baseUrl}/models"
            : $"{baseUrl}/v1/models";
    }

    /// <summary>按协议构造鉴权头：Anthropic 系用 x-api-key + anthropic-version，其余用 Bearer</summary>
    private static HttpRequestMessage BuildRequest(string endpoint, string? api, string apiKey) {
        var message = new HttpRequestMessage(HttpMethod.Get, endpoint);
        if (IsAnthropic(api)) {
            message.Headers.TryAddWithoutValidation("x-api-key", apiKey);
            message.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
        } else {
            message.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        }
        return message;
    }

    /// <summary>协议是否属于 Anthropic messages（仅该协议用 x-api-key 与版本头）</summary>
    private static bool IsAnthropic(string? api) =>
        string.Equals(api?.Trim(), "anthropic-messages", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// 解析供应商响应：兼容 OpenAI（data[].id / context_length / max_tokens 顶层）
    /// 与 Anthropic（data[].id / display_name / max_tokens）两种形状，只保留可映射字段。
    /// </summary>
    /// <param name="body">供应商 JSON</param>
    internal static List<DshDiscoveredModel> ParseModels(string body) {
        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array) {
            return [];
        }
        var models = new List<DshDiscoveredModel>();
        foreach (var item in data.EnumerateArray()) {
            if (item.ValueKind != JsonValueKind.Object || ReadString(item, "id") is not { Length: > 0 } id) {
                continue;
            }
            models.Add(new DshDiscoveredModel {
                Id = id,
                Name = ReadString(item, "name") ?? ReadString(item, "display_name"),
                ContextWindow = ReadInt(item, "context_length") ?? ReadInt(item, "context_window"),
                MaxTokens = ReadInt(item, "max_tokens") ?? ReadInt(item, "max_output_tokens"),
                InputModalities = ReadModalities(item),
            });
            if (models.Count >= ModelLimit) {
                break;
            }
        }
        return models;
    }

    /// <summary>读字符串字段（缺失/非字符串返回 null）</summary>
    private static string? ReadString(JsonElement item, string name) =>
        item.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>读整数 token 字段（容忍数字字符串）</summary>
    private static int? ReadInt(JsonElement item, string name) {
        if (!item.TryGetProperty(name, out var value)) {
            return null;
        }
        return value.ValueKind switch {
            JsonValueKind.Number when value.TryGetInt32(out var number) => number,
            JsonValueKind.String when int.TryParse(value.GetString(), out var parsed) => parsed,
            _ => null,
        };
    }

    /// <summary>读输入模态（只收 text/image，未知词汇丢弃；无有效值时返回空数组）</summary>
    private static List<string> ReadModalities(JsonElement item) {
        var source = item.TryGetProperty("input_modalities", out var direct) && direct.ValueKind == JsonValueKind.Array
            ? direct
            : item.TryGetProperty("architecture", out var architecture)
                && architecture.ValueKind == JsonValueKind.Object
                && architecture.TryGetProperty("input_modalities", out var nested)
                && nested.ValueKind == JsonValueKind.Array
                    ? nested
                    : default;
        if (source.ValueKind != JsonValueKind.Array) {
            return [];
        }
        return source.EnumerateArray()
            .Where(entry => entry.ValueKind == JsonValueKind.String)
            .Select(entry => entry.GetString() ?? "")
            .Where(entry => entry is "text" or "image")
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>错误提示里的供应商描述</summary>
    private static string DescribeProvider(DshModelDiscoveryRequest request) =>
        string.IsNullOrWhiteSpace(request.Provider) ? "(未指定)" : request.Provider;

    /// <summary>响应片段截断（错误引导里指认供应商到底回了什么）</summary>
    private static string Truncate(string body) => body.Length <= 200 ? body : body[..200] + "…";
}
