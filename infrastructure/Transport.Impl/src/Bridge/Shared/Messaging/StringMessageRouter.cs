using JoinCode.Abstractions.Attributes;

namespace JoinCode.Transport.Bridge;

/// <summary>
/// 字符串消息路由器 - 纯字符串级别的消息去重和分发
/// 不依赖任何业务消息类型（如 BridgeMessage），可独立于 Bridge 使用
/// </summary>
[Register]
public sealed partial class StringMessageRouter : IMessageRouter
{
    private readonly ILogger? _logger;
    private readonly BoundedUUIDSet _processedMessageIds;

    /// <summary>
    /// 接收到去重后的字符串消息
    /// </summary>
    public event EventHandler<StringMessageReceivedEventArgs>? MessageReceived;

    public StringMessageRouter(
        TransportConfiguration config,
        ILogger? logger = null)
    {
        _logger = logger;
        _processedMessageIds = new BoundedUUIDSet(config.MessageDeduplicationCapacity);
    }

    /// <summary>
    /// 处理接收到的字符串消息
    /// 通过 messageIdExtractor 从原始消息中提取 ID 进行去重
    /// </summary>
    /// <param name="messageJson">原始消息字符串</param>
    /// <param name="messageIdExtractor">从消息中提取 ID 的委托，返回 null 表示无法提取</param>
    /// <param name="cancellationToken">取消令牌</param>
    public async Task ProcessMessageAsync(
        string messageJson,
        Func<string, string?> messageIdExtractor,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var messageId = messageIdExtractor(messageJson);

            if (messageId is null)
            {
                _logger?.LogWarning("[StringMessageRouter] 无法提取消息ID: {Message}", messageJson);
                return;
            }

            // 消息去重检查
            if (!await _processedMessageIds.AddAsync(messageId, cancellationToken).ConfigureAwait(false))
            {
                _logger?.LogDebug("[StringMessageRouter] 忽略重复消息: {MessageId}", messageId);
                return;
            }

            MessageReceived?.Invoke(this, new StringMessageReceivedEventArgs(messageJson, messageId));
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "[StringMessageRouter] 处理消息失败");
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _processedMessageIds.DisposeAsync().ConfigureAwait(false);
    }
}

// StringMessageReceivedEventArgs 已迁移到 JoinCode.Transport.Bridge 命名空间 (Transport.Contracts)
