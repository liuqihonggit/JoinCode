namespace Core.Agents.Coordinator;

/// <summary>
/// 飞书机器人适配器 — 通过飞书开放平台 Bot API 发送/接收消息。
/// <para>发送：POST https://open.feishu.cn/open-apis/im/v1/messages?receive_id_type=chat_id</para>
/// <para>认证：Bearer {tenant_access_token}（通过 app_id + app_secret 获取）</para>
/// <para>接收：事件订阅 webhook（飞书事件回调），监听 im.message.receive_v1 事件</para>
/// <para>配置：appId + appSecret 通过 <see cref="FeishuBotConfig"/> 注入</para>
/// </summary>
public sealed class FeishuBotAdapter : PlatformBotAdapterBase<FeishuBotConfig>
{
    /// <summary>
    /// 构造飞书 Bot 适配器。
    /// </summary>
    /// <param name="config">飞书 Bot 配置</param>
    /// <param name="httpClient">HTTP 客户端</param>
    /// <param name="logger">日志记录器</param>
    public FeishuBotAdapter(FeishuBotConfig config, HttpClient httpClient, ILogger<FeishuBotAdapter>? logger = null)
        : base(config, httpClient, logger)
    {
    }

    /// <inheritdoc/>
    public override string PlatformName => "feishu";

    /// <inheritdoc/>
    protected override async ValueTask<string> AcquireTokenAsync(CancellationToken ct)
    {
        var url = $"{Config.ApiBaseUrl}/open-apis/auth/v3/tenant_access_token/internal";
        var body = $$"""{"app_id":"{{Config.AppId}}","app_secret":"{{Config.AppSecret}}"}""";
        using var response = await HttpClient.PostAsync(url, new StringContent(body, Encoding.UTF8, "application/json"), ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        var json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("tenant_access_token").GetString()!;
    }

    /// <inheritdoc/>
    protected override string BuildSendUrl(string targetId)
        => $"{Config.ApiBaseUrl}/open-apis/im/v1/messages?receive_id_type=chat_id";

    /// <inheritdoc/>
    protected override string BuildSendContent(string targetId, string text)
        => $$"""{"receive_id":"{{targetId}}","msg_type":"text","content":"{\"text\":\"{{JsonEncodedText.Encode(text)}}\"}"}""";

    /// <inheritdoc/>
    protected override string? ExtractMessageId(string json)
    {
        var doc = JsonDocument.Parse(json);
        if (doc.RootElement.TryGetProperty("data", out var data)
            && data.TryGetProperty("message_id", out var id))
        {
            return id.GetString();
        }
        return null;
    }
}

/// <summary>
/// 飞书 Bot 配置 — API 凭据与端点。
/// </summary>
public sealed record FeishuBotConfig
{
    /// <summary>飞书 App ID。</summary>
    public required string AppId { get; init; }

    /// <summary>飞书 App Secret。</summary>
    public required string AppSecret { get; init; }

    /// <summary>API 基地址（默认 https://open.feishu.cn）。</summary>
    public string ApiBaseUrl { get; init; } = "https://open.feishu.cn";
}
