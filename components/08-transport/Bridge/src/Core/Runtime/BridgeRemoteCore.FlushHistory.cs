
namespace Core.Bridge;

public static partial class BridgeRemoteCore
{
    #region flushHistory + drainFlushGate

    /// <summary>
    /// 刷新初始历史消息 — 对齐 TS 端 flushHistory()
    /// 过滤 + 截断 + 转换 + 写入
    /// </summary>
    internal static async Task FlushHistoryAsync(
        string[] messages,
        int initialHistoryCap,
        Func<string, string[]>? toSDKMessages,
        IReplBridgeTransport transport,
        string sessionId,
        CancellationToken ct,
        HashSet<string>? previouslyFlushedUUIDs = null)
    {
        // 对齐 TS 端: eligible = msgs.filter(isEligibleBridgeMessage)
        // C# 端: messages 已经是过滤后的字符串数组，直接使用
        var eligible = messages;

        // 对齐 TS 端: initialHistoryCap 截断
        if (initialHistoryCap > 0 && eligible.Length > initialHistoryCap)
        {
            eligible = eligible[^initialHistoryCap..];
        }

        if (eligible.Length == 0) return;

        // 对齐 TS 端: previouslyFlushedUUIDs 过滤 — 排除已刷新的消息防止重复发送
        // 重复 UUID 会导致服务器杀死 WebSocket 连接
        if (previouslyFlushedUUIDs is not null)
        {
            eligible = eligible.Where(m =>
            {
                var uuid = BridgeMessaging.ExtractUuid(m);
                return uuid is null || !previouslyFlushedUUIDs.Contains(uuid);
            }).ToArray();

            if (eligible.Length == 0) return;
        }

        // 对齐 TS 端: toSDKMessages(capped).map(m => ({...m, session_id: sessionId}))
        if (toSDKMessages is not null)
        {
            var events = new List<string>(eligible.Length * 2);
            foreach (var msg in eligible)
            {
                var sdkMsgs = toSDKMessages(msg);
                foreach (var sdkMsg in sdkMsgs)
                {
                    events.Add(BridgeMessaging.InjectSessionId(sdkMsg, sessionId));
                }
            }

            if (events.Count > 0)
            {
                // 对齐 TS 端: snapshot droppedBatchCount before writeBatch
                var dropsBefore = transport.DroppedBatchCount;
                await transport.WriteBatchAsync(events, ct).ConfigureAwait(false);

                // 对齐 TS 端: 如果批次被丢弃，不标记 UUID — 保持可重发
                if (transport.DroppedBatchCount > dropsBefore)
                {
                    return;
                }

                // 对齐 TS 端: flush 成功后标记 UUID — 防止重连时重复发送
                if (previouslyFlushedUUIDs is not null)
                {
                    foreach (var evt in events)
                    {
                        var uuid = BridgeMessaging.ExtractUuid(evt);
                        if (uuid is not null)
                        {
                            previouslyFlushedUUIDs.Add(uuid);
                        }
                    }
                }
            }
        }
        else
        {
            var events = new string[eligible.Length];
            for (var i = 0; i < eligible.Length; i++)
            {
                events[i] = BridgeMessaging.InjectSessionId(eligible[i], sessionId);
            }
            // 对齐 TS 端: snapshot droppedBatchCount before writeBatch
            var dropsBefore = transport.DroppedBatchCount;
            await transport.WriteBatchAsync(events, ct).ConfigureAwait(false);

            // 对齐 TS 端: 如果批次被丢弃，不标记 UUID
            if (transport.DroppedBatchCount > dropsBefore)
            {
                return;
            }

            // 对齐 TS 端: flush 成功后标记 UUID
            if (previouslyFlushedUUIDs is not null)
            {
                foreach (var evt in events)
                {
                    var uuid = BridgeMessaging.ExtractUuid(evt);
                    if (uuid is not null)
                    {
                        previouslyFlushedUUIDs.Add(uuid);
                    }
                }
            }
        }
    }

    /// <summary>
    /// 排空刷新门控中的排队消息 — 对齐 TS 端 drainFlushGate()
    /// </summary>
    internal static void DrainFlushGate(
        BridgeFlushGate<string> flushGate,
        BoundedUUIDSet recentPostedUUIDs,
        Func<string, string[]>? toSDKMessages,
        IReplBridgeTransport transport,
        string sessionId,
        CancellationToken ct = default)
    {
        var msgs = flushGate.End();
        if (msgs.Length == 0) return;

        // 对齐 TS 端: for (const msg of msgs) recentPostedUUIDs.add(msg.uuid)
        foreach (var msg in msgs)
        {
            var uuid = BridgeMessaging.ExtractUuid(msg);
            if (uuid is not null)
            {
                recentPostedUUIDs.Add(uuid);
            }
        }

        // 对齐 TS 端: toSDKMessages(msgs).map(m => ({...m, session_id: sessionId}))
        if (toSDKMessages is not null)
        {
            var events = new List<string>(msgs.Length * 2);
            foreach (var msg in msgs)
            {
                var sdkMsgs = toSDKMessages(msg);
                foreach (var sdkMsg in sdkMsgs)
                {
                    events.Add(BridgeMessaging.InjectSessionId(sdkMsg, sessionId));
                }
            }

            if (events.Count > 0)
            {
                _ = transport.WriteBatchAsync(events, ct);
            }
        }
        else
        {
            var events = new string[msgs.Length];
            for (var i = 0; i < msgs.Length; i++)
            {
                events[i] = BridgeMessaging.InjectSessionId(msgs[i], sessionId);
            }
            _ = transport.WriteBatchAsync(events, ct);
        }
    }

    #endregion
}
