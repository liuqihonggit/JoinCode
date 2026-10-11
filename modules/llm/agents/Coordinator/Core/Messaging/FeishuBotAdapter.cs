// JCC1017 抑制: 存量手写 JSON, 后续改为 DTO+JsonContext
// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC1017, JCC11003
// JCC11003 抑制: 存量代码可空抑制, 后续逐步修复
#pragma warning disable JCC11003
namespace Core.Agents.Coordinator;

/// <summary>
/// 飞书机器人适配器 — 通过飞书开放平台 Bot API 发送/接收消息。
/// <para>发送：POST https://open.feishu.cn/open-apis/im/v1/messages?receive_id_type=chat_id</para>
/// <para>认证：Bearer {tenant_access_token}（通过 app_id + app_secret 获取）</para>
/// <para>接收：事件订阅 webhook（飞书事件回调），监听 im.message.receive_v1 事件</para>
/// <para>配置：appId + appSecret 通过 <see cref="FeishuBotConfig"/> 注入</para>
/// </summary>
public sealed class FeishuBotAdapter : PlatformBotAdapterBase<FeishuBotConfig> {
    /// <summary>
    /// 构造飞书 Bot 适配器。
    /// </summary>
    /// <param name="config">飞书 Bot 配置</param>
    /// <param name="httpClient">HTTP 客户端</param>
    /// <param name="logger">日志记录器</param>
    public FeishuBotAdapter(FeishuBotConfig config, HttpClient httpClient, ILogger<FeishuBotAdapter>? logger = null)
        : base(config, httpClient, logger) {
    }

    /// <inheritdoc/>
    public override string PlatformName => "feishu";

    /// <inheritdoc/>
    protected override async ValueTask<string> AcquireTokenAsync(CancellationToken ct) {
        var url = $"{Config.ApiBaseUrl}/open-apis/auth/v3/tenant_access_token/internal";
        var body = $$"""{"app_id":"{{Config.AppId}}","app_secret":"{{Config.AppSecret}}"}""";
        using var response = await HttpClient.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("tenant_access_token").GetString()!;
    }

    /// <inheritdoc/>
    protected override string BuildSendUrl(string targetId)
        => $"{Config.ApiBaseUrl}/open-apis/im/v1/messages?receive_id_type=chat_id";

    /// <inheritdoc/>
    protected override string BuildSendContent(string targetId, string text) {
        var innerContent = JsonSerializer.Serialize(new FeishuMessageContentDto { Text = text }, BotAdapterJsonContext.Default.FeishuMessageContentDto);
        return JsonSerializer.Serialize(new FeishuSendMessageDto { ReceiveId = targetId, MsgType = "text", Content = innerContent }, BotAdapterJsonContext.Default.FeishuSendMessageDto);
    }

    /// <inheritdoc/>
    protected override string? ExtractMessageId(string json) {
        try {
            var dto = JsonSerializer.Deserialize(json, BotAdapterJsonContext.Default.FeishuMessageCallbackDto);
            return dto?.Data?.MessageId;
        } catch (JsonException) {
            return null;
        }
    }
}

/// <summary>
/// 飞书 Bot 配置 — API 凭据与端点。
/// </summary>
public sealed record FeishuBotConfig {
    /// <summary>飞书 App ID。</summary>
    public required string AppId { get; init; }

    /// <summary>飞书 App Secret。</summary>
    public required string AppSecret { get; init; }

    /// <summary>API 基地址（默认 https://open.feishu.cn）。</summary>
    public string ApiBaseUrl { get; init; } = "https://open.feishu.cn";
}

/// <summary>飞书消息回调 DTO — 解析事件回调 JSON {data: {message_id}}</summary>
public sealed class FeishuMessageCallbackDto {
    /// <summary>回调数据体。</summary>
    [JsonPropertyName("data")]
    public FeishuMessageCallbackDataDto? Data { get; init; }
}

/// <summary>飞书消息回调数据体 DTO。</summary>
public sealed class FeishuMessageCallbackDataDto {
    /// <summary>消息 ID。</summary>
    [JsonPropertyName("message_id")]
    public string? MessageId { get; init; }
}