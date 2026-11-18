// web_fetch 的二级模型调用 —— 纯 BCL HttpClient 直调 OpenAI-compatible /chat/completions。
// 与 code 组 CodeLlm、media 组 MediaLlm 同一套做法；三处重复是依赖墙的代价（每个 exe 只引契约层，
// 不共享 Infrastructure），改这里时请同步那两处（有测试覆盖的部分在宿主侧）。




namespace JoinCode.CliNode.Web;

internal sealed record WebLlmConfig(string Endpoint, string Model, string ApiKey);

[JsonSerializable(typeof(WebSettingsDoc))]
[JsonSerializable(typeof(WebChatRequest))]
[JsonSerializable(typeof(WebChatResponse))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal sealed partial class WebLlmJsonContext : JsonSerializerContext;

internal sealed class WebSettingsDoc {
    public WebCurrentConfig? Current { get; set; }
    public Dictionary<string, WebVendorCfg>? Vendor { get; set; }
}
internal sealed class WebCurrentConfig {
    public string? Profile { get; set; }
}
internal sealed class WebVendorCfg {
    public string? Endpoint { get; set; }
    public string? Model { get; set; }
    public string? ApiKeyEnvVar { get; set; }
}

internal sealed class WebChatRequest {
    [JsonPropertyName("model")] public string Model { get; set; } = "";
    [JsonPropertyName("messages")] public List<WebChatMsg> Messages { get; set; } = [];
    [JsonPropertyName("temperature")] public float Temperature { get; set; }
    [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; } = 4096;
}
internal sealed class WebChatMsg {
    [JsonPropertyName("role")] public string Role { get; set; } = "user";
    [JsonPropertyName("content")] public string Content { get; set; } = "";
}
internal sealed class WebChatResponse {
    [JsonPropertyName("choices")] public List<WebChoice>? Choices { get; set; }
    [JsonPropertyName("error")] public WebErrorInfo? Error { get; set; }
}
internal sealed class WebChoice {
    [JsonPropertyName("message")] public WebResponseMessage? Message { get; set; }
}
internal sealed class WebResponseMessage {
    [JsonPropertyName("content")] public string? Content { get; set; }
}
internal sealed class WebErrorInfo {
    [JsonPropertyName("message")] public string? Message { get; set; }
}

internal static class WebLlm {
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(3) };

    /// <summary>
    /// 应用数据目录下的 settings.json —— 与宿主 <c>AppDataPaths</c> 同一套解析：
    /// 目录名读环境变量 <c>JCC_APP_DATA_FOLDER</c>（默认 <c>.jcc</c>），相对路径挂在用户主目录下。
    /// 少了这一步，<c>JCC_APP_DATA_FOLDER</c> 指向别处的多配置环境就会读到错的那份。
    /// </summary>
    public static string SettingsPath() {
        var folder = Environment.GetEnvironmentVariable("JCC_APP_DATA_FOLDER");
        if (string.IsNullOrWhiteSpace(folder)) folder = ".jcc";
        var directory = Path.IsPathRooted(folder)
            ? folder
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), folder);
        var fileName = Environment.GetEnvironmentVariable("JCC_SETTINGS_FILE_NAME");
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "settings.json";
        return Path.Combine(directory, fileName);
    }

    /// <summary>解析 settings.json 的 current profile 与对应 vendor；缺配置时抛可执行引导。</summary>
    public static WebLlmConfig ResolveConfig() {
        var settingsPath = SettingsPath();
        if (!File.Exists(settingsPath))
            throw new ArgumentException($"未找到 LLM 配置 {settingsPath}。请先运行 jcc 完成模型配置");

        WebSettingsDoc? doc;
        try {
            doc = JsonSerializer.Deserialize(File.ReadAllText(settingsPath).TrimStart('\uFEFF'), WebLlmJsonContext.Default.WebSettingsDoc);
        } catch (JsonException ex) {
            throw new ArgumentException($"LLM 配置解析失败: {ex.Message}");
        }

        var profile = doc?.Current?.Profile ?? "";
        var vendorName = profile.Split('/')[0];
        if (vendorName.Length == 0 || doc?.Vendor is null || !doc.Vendor.TryGetValue(vendorName, out var vendor) || vendor is null)
            throw new ArgumentException($"LLM 配置里的 current profile '{profile}' 没有对应的 vendor 配置。");

        var endpoint = (vendor.Endpoint ?? "").TrimEnd('/');
        var model = vendor.Model ?? "";
        var envVar = vendor.ApiKeyEnvVar ?? "OPENAI_API_KEY";
        if (endpoint.Length == 0 || model.Length == 0)
            throw new ArgumentException($"vendor '{vendorName}' 缺少 endpoint/model 配置。");
        var apiKey = Environment.GetEnvironmentVariable(envVar) ?? "";
        if (apiKey.Length == 0)
            throw new ArgumentException($"未设置环境变量 {envVar}（vendor '{vendorName}' 的 API key），web_fetch 的二级模型调用不可用。");
        return new WebLlmConfig(endpoint, model, apiKey);
    }

    /// <summary>单轮 user 消息，返回模型回复文本。</summary>
    public static async Task<string> AskAsync(string userPrompt, CancellationToken ct) {
        var config = ResolveConfig();
        var request = new WebChatRequest {
            Model = config.Model,
            Messages = [new WebChatMsg { Role = "user", Content = userPrompt }]
        };

        var json = JsonSerializer.Serialize(request, WebLlmJsonContext.Default.WebChatRequest);
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, config.Endpoint + "/chat/completions");
        httpRequest.Headers.TryAddWithoutValidation("Authorization", "Bearer " + config.ApiKey);
        httpRequest.Content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await Http.SendAsync(httpRequest, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        WebChatResponse? parsed;
        try {
            parsed = JsonSerializer.Deserialize(body, WebLlmJsonContext.Default.WebChatResponse);
        } catch (JsonException) {
            throw new ArgumentException($"LLM 返回非 JSON: {body[..Math.Min(200, body.Length)]}");
        }
        if (parsed?.Error?.Message is { Length: > 0 } error) throw new ArgumentException($"LLM API 错误: {error}");
        var text = parsed?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("LLM 返回空响应");
        return text;
    }
}
