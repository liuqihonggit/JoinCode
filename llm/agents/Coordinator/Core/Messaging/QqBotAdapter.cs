namespace Core.Agents.Coordinator;

/// <summary>
/// QQ 频道机器人适配器 — 通过 QQ Bot API 发送/接收消息。
/// <para>发送：POST https://api.sgroup.qq.com/channels/{{channel_id}}/messages</para>
/// <para>认证：Bearer {accessToken}（QQ Bot WebSocket 鉴权后获取）</para>
/// <para>接收：WebSocket 长连接（QQ Bot Gateway），监听 MESSAGE_CREATE 事件</para>
/// <para>配置：appId + appSecret 通过 <see cref="QqBotConfig"/> 注入</para>
/// </summary>
public sealed class QqBotAdapter : PlatformBotAdapterBase<QqBotConfig> {
    /// <summary>
    /// 构造 QQ Bot 适配器。
    /// </summary>
    /// <param name="config">QQ Bot 配置（appId/appSecret/基地址）</param>
    /// <param name="httpClient">HTTP 客户端（调用方管理生命周期）</param>
    /// <param name="logger">日志记录器</param>
    public QqBotAdapter(QqBotConfig config, HttpClient httpClient, ILogger<QqBotAdapter>? logger = null)
        : base(config, httpClient, logger) {
    }

    /// <inheritdoc/>
    public override string PlatformName => "qq";

    /// <inheritdoc/>
    protected override async ValueTask<string> AcquireTokenAsync(CancellationToken ct) {
        var url = $"{Config.AuthBaseUrl}/app/getAppAccessToken";
        var body = $$"""{"appId":"{{Config.AppId}}","appSecret":"{{Config.AppSecret}}"}""";
        using var response = await HttpClient.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("access_token").GetString()!;
    }

    /// <inheritdoc/>
    protected override string BuildSendUrl(string targetId)
        => $"{Config.ApiBaseUrl}/channels/{targetId}/messages";

    /// <inheritdoc/>
    protected override string BuildSendContent(string targetId, string text)
        => JsonSerializer.Serialize(new QqSendMessageDto { Content = text }, BotAdapterJsonContext.Default.QqSendMessageDto);

    /// <inheritdoc/>
    protected override string? ExtractMessageId(string json) {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.TryGetProperty("id", out var id) ? id.GetString() : null;
    }
}

/// <summary>
/// QQ Bot 配置 — API 凭据与端点。
/// </summary>
public sealed record QqBotConfig {
    /// <summary>QQ Bot AppID。</summary>
    public required string AppId { get; init; }

    /// <summary>QQ Bot AppSecret。</summary>
    public required string AppSecret { get; init; }

    /// <summary>API 基地址（默认 https://api.sgroup.qq.com）。</summary>
    public string ApiBaseUrl { get; init; } = "https://api.sgroup.qq.com";

    /// <summary>鉴权基地址（默认 https://bots.qq.com）。</summary>
    public string AuthBaseUrl { get; init; } = "https://bots.qq.com";
}

/// <summary>QQ Bot 发送消息 DTO</summary>
internal sealed class QqSendMessageDto {
    /// <summary>消息内容</summary>
    [JsonPropertyName("content")]
    public required string Content { get; init; }
}

/// <summary>飞书发送消息 DTO</summary>
internal sealed class FeishuSendMessageDto {
    /// <summary>接收者 ID</summary>
    [JsonPropertyName("receive_id")]
    public required string ReceiveId { get; init; }
    /// <summary>消息类型</summary>
    [JsonPropertyName("msg_type")]
    public required string MsgType { get; init; }
    /// <summary>消息内容(JSON 字符串)</summary>
    [JsonPropertyName("content")]
    public required string Content { get; init; }
}

/// <summary>飞书消息内容 DTO</summary>
internal sealed class FeishuMessageContentDto {
    /// <summary>文本内容</summary>
    [JsonPropertyName("text")]
    public required string Text { get; init; }
}

/// <summary>Bot 适配器 JSON 序列化上下文</summary>
[JsonSourceGenerationOptions(WriteIndented = false)]
[JsonSerializable(typeof(QqSendMessageDto))]
[JsonSerializable(typeof(FeishuSendMessageDto))]
[JsonSerializable(typeof(FeishuMessageContentDto))]
internal sealed partial class BotAdapterJsonContext : JsonSerializerContext;