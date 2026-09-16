namespace Core.Agents.Coordinator;

/// <summary>
/// 平台机器人适配器接口 — 抽象 QQ/飞书/Discord 等外部消息平台的 Bot API。
/// <para><see cref="NetworkMailbox"/> 通过此接口与外部平台通信，平台差异由具体适配器封装。</para>
/// <para>发送：<see cref="SendAsync"/> 调用平台 Bot API 投递消息。</para>
/// <para>接收：<see cref="ReceiveAsync"/> 从平台 webhook/WebSocket 长轮询接收消息。</para>
/// </summary>
public interface IPlatformBotAdapter : IAsyncDisposable
{
    /// <summary>平台名称（如 "qq"/"feishu"/"discord"）。</summary>
    string PlatformName { get; }

    /// <summary>适配器是否已连接/就绪。</summary>
    bool IsConnected { get; }

    /// <summary>
    /// 启动适配器 — 建立连接、认证、开始接收循环。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    ValueTask StartAsync(CancellationToken ct = default);

    /// <summary>
    /// 发送消息到平台指定目标（频道/群/用户）。
    /// </summary>
    /// <param name="targetId">目标标识（频道ID/群ID/用户ID）</param>
    /// <param name="text">消息文本</param>
    /// <param name="ct">取消令牌</param>
    /// <returns>平台返回的消息ID（失败返回null）</returns>
    ValueTask<string?> SendAsync(string targetId, string text, CancellationToken ct = default);

    /// <summary>
    /// 从平台接收消息流 — 阻塞式 IAsyncEnumerable，适配器运行期间持续产出。
    /// </summary>
    /// <param name="ct">取消令牌</param>
    /// <returns>消息流（源ID=发送者标识, 文本=消息内容）</returns>
    IAsyncEnumerable<PlatformMessage> ReceiveAsync(CancellationToken ct = default);
}

/// <summary>
/// 平台消息 — 从外部平台接收到的消息。
/// </summary>
/// <param name="SourceId">发送者标识（平台用户ID/频道ID）</param>
/// <param name="TargetId">目标标识（频道ID/群ID）</param>
/// <param name="Text">消息文本内容</param>
/// <param name="Timestamp">消息时间戳</param>
public sealed record PlatformMessage(string SourceId, string TargetId, string Text, DateTimeOffset Timestamp);
