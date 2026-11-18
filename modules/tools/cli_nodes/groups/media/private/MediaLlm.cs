// 纯 BCL 的 OpenAI-compatible LLM 客户端 — 用 HttpClient 直调 /chat/completions，
// 从 ~/.jcc/settings.json 读 endpoint/model/apiKeyEnvVar，密钥从环境变量取。
// 不引用任何 LLM SDK / 引擎程序集：jcc 主进程无需为 vision 工具初始化引擎。




namespace JoinCode.CliNode.Media;

internal sealed record LlmConfig(string Endpoint, string Model, string ApiKey);

[JsonSerializable(typeof(SettingsDoc))]
[JsonSerializable(typeof(ChatRequest))]
[JsonSerializable(typeof(ChatResponse))]
[JsonSerializable(typeof(ResponseMessage))]
[JsonSerializable(typeof(ContentPart))]
[JsonSerializable(typeof(ImageUrl))]
[JsonSerializable(typeof(object[]))]
[JsonSerializable(typeof(List<ContentPart>))]
[JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
internal sealed partial class MediaLlmJsonContext : JsonSerializerContext;

internal sealed class SettingsDoc {
    public CurrentConfig? Current { get; set; }
    public Dictionary<string, VendorCfg>? Vendor { get; set; }
}
internal sealed class CurrentConfig {
    public string? Profile { get; set; }
}
internal sealed class VendorCfg {
    public string? Endpoint { get; set; }
    public string? Model { get; set; }
    public string? ApiKeyEnvVar { get; set; }
}

internal sealed class ChatRequest {
    [JsonPropertyName("model")] public string Model { get; set; } = "";
    [JsonPropertyName("messages")] public List<ChatMsg> Messages { get; set; } = [];
    [JsonPropertyName("temperature")] public float Temperature { get; set; } = 0.3f;
    [JsonPropertyName("max_tokens")] public int MaxTokens { get; set; } = 4000;
}
internal sealed class ChatMsg {
    [JsonPropertyName("role")] public string Role { get; set; } = "user";
    [JsonPropertyName("content")] public object Content { get; set; } = "";
}
internal sealed class ChatResponse {
    [JsonPropertyName("choices")] public List<Choice>? Choices { get; set; }
    [JsonPropertyName("error")] public ErrorInfo? Error { get; set; }
}
internal sealed class Choice {
    [JsonPropertyName("message")] public ResponseMessage? Message { get; set; }
}
internal sealed class ResponseMessage {
    [JsonPropertyName("role")] public string? Role { get; set; }
    [JsonPropertyName("content")] public string? Content { get; set; }
}
internal sealed class ErrorInfo {
    [JsonPropertyName("message")] public string? Message { get; set; }
}

/// <summary>content part（文本/图片 URL）。</summary>
internal sealed class ContentPart {
    [JsonPropertyName("type")] public string Type { get; set; } = "text";
    [JsonPropertyName("text")] public string? Text { get; set; }
    [JsonPropertyName("image_url")] public ImageUrl? ImageUrl { get; set; }
}
internal sealed class ImageUrl {
    [JsonPropertyName("url")] public string Url { get; set; } = "";
}

internal static class MediaLlm {
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(3) };

    /// <summary>从 settings.json + 环境变量解析 LLM 配置。profile 支持 "vendor/profile" 形式取第一段。</summary>
    public static LlmConfig ResolveConfig() {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var settingsPath = Path.Combine(home, ".jcc", "settings.json");
        if (!File.Exists(settingsPath))
            throw new ArgumentException($"未找到 LLM 配置 {settingsPath}。请先运行 jcc 完成模型配置");

        SettingsDoc? doc;
        try {
            // settings.json 可能带 UTF-8 BOM，读文本时去掉。
            doc = JsonSerializer.Deserialize(File.ReadAllText(settingsPath).TrimStart('\uFEFF'), MediaLlmJsonContext.Default.SettingsDoc);
        } catch (JsonException ex) {
            throw new ArgumentException($"LLM 配置解析失败: {ex.Message}");
        }
        var profile = doc?.Current?.Profile ?? "";
        var vendorName = profile.Split('/')[0];
        if (string.IsNullOrEmpty(vendorName) || doc?.Vendor is null || !doc.Vendor.TryGetValue(vendorName, out var v) || v is null)
            throw new ArgumentException($"LLM 配置里的 current profile '{profile}' 无对应 vendor 配置");

        var endpoint = (v.Endpoint ?? "").TrimEnd('/');
        var model = v.Model ?? "";
        var envVar = v.ApiKeyEnvVar ?? "OPENAI_API_KEY";
        var apiKey = Environment.GetEnvironmentVariable(envVar) ?? "";
        if (endpoint.Length == 0 || model.Length == 0)
            throw new ArgumentException($"vendor '{vendorName}' 缺少 endpoint/model 配置");
        if (apiKey.Length == 0)
            throw new ArgumentException($"未设置环境变量 {envVar}（vendor '{vendorName}' 的 API key）。请先 export {envVar}");
        return new LlmConfig(endpoint, model, apiKey);
    }

    /// <summary>发文本 prompt，返回模型回复文本。</summary>
    public static async Task<string> AskTextAsync(string system, string user, CancellationToken ct) {
        var cfg = ResolveConfig();
        var req = new ChatRequest {
            Model = cfg.Model,
            Messages = [
                new ChatMsg { Role = "system", Content = system },
                new ChatMsg { Role = "user", Content = user }
            ]
        };
        return await SendAsync(cfg, req, ct).ConfigureAwait(false);
    }

    /// <summary>发 vision（文本 + base64 图片），返回模型回复文本。</summary>
    public static async Task<string> AskVisionAsync(string system, string userText, string base64Image, string mime, CancellationToken ct) {
        var cfg = ResolveConfig();
        var parts = new object[] {
            new ContentPart { Type = "text", Text = userText },
            new ContentPart { Type = "image_url", ImageUrl = new ImageUrl { Url = $"data:{mime};base64,{base64Image}" } }
        };
        var req = new ChatRequest {
            Model = cfg.Model,
            Messages = [
                new ChatMsg { Role = "system", Content = system },
                new ChatMsg { Role = "user", Content = parts }
            ]
        };
        return await SendAsync(cfg, req, ct).ConfigureAwait(false);
    }

    private static async Task<string> SendAsync(LlmConfig cfg, ChatRequest req, CancellationToken ct) {
        var json = JsonSerializer.Serialize(req, MediaLlmJsonContext.Default.ChatRequest);
        using var httpReq = new HttpRequestMessage(HttpMethod.Post, cfg.Endpoint + "/chat/completions");
        httpReq.Headers.TryAddWithoutValidation("Authorization", "Bearer " + cfg.ApiKey);
        httpReq.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await HttpClient.SendAsync(httpReq, ct).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        ChatResponse? parsed;
        try { parsed = JsonSerializer.Deserialize(body, MediaLlmJsonContext.Default.ChatResponse); }
        catch (JsonException) { throw new ArgumentException($"LLM 返回非 JSON: {body[..Math.Min(200, body.Length)]}"); }
        if (parsed?.Error?.Message is { Length: > 0 } err)
            throw new ArgumentException($"LLM API 错误: {err}");
        var text = parsed?.Choices?.FirstOrDefault()?.Message?.Content;
        if (string.IsNullOrWhiteSpace(text))
            throw new ArgumentException("LLM 返回空响应");
        return text;
    }

    /// <summary>宽容解析 LLM 返回的 JSON（去 markdown 代码围栏后 Deserialize）。</summary>
    public static T? ParseLoose<T>(string text, JsonTypeInfo<T> typeInfo) {
        var cleaned = text.Trim();
        if (cleaned.StartsWith("```")) {
            var nl = cleaned.IndexOf('\n');
            if (nl >= 0) cleaned = cleaned[(nl + 1)..];
            var fence = cleaned.LastIndexOf("```");
            if (fence >= 0) cleaned = cleaned[..fence];
        }
        try { return JsonSerializer.Deserialize(cleaned, typeInfo); }
        catch (JsonException) { return default; }
    }
}
