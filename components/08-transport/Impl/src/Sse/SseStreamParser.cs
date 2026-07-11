namespace JoinCode.Transport;

/// <summary>
/// SSE 事件流解析器 — 从 Stream 中解析 SSE 事件
/// </summary>
/// <remarks>
/// 对齐 SSE 规范: event:/data:/空行分隔，多行 data 用换行拼接。
/// 不依赖任何上层协议类型，输出结构化的 SseEvent。
/// </remarks>
public sealed class SseStreamParser
{
    /// <summary>
    /// 从流中异步枚举 SSE 事件
    /// </summary>
    public static async IAsyncEnumerable<SseEvent> ParseAsync(
        Stream stream,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);

        var eventType = string.Empty;
        var dataBuilder = new StringBuilder();

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) break; // 流关闭

            if (string.IsNullOrEmpty(line))
            {
                // 空行 = 事件结束
                if (dataBuilder.Length > 0)
                {
                    yield return new SseEvent(eventType, dataBuilder.ToString());
                    dataBuilder.Clear();
                    eventType = string.Empty;
                }
                continue;
            }

            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                eventType = line.AsSpan(6).Trim().ToString();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (dataBuilder.Length > 0) dataBuilder.AppendLine();
                dataBuilder.Append(line.AsSpan(5).Trim());
            }
            // 忽略 id:、retry: 等其他 SSE 字段
        }

        // 流结束时，输出最后一个未完成的事件
        if (dataBuilder.Length > 0)
        {
            yield return new SseEvent(eventType, dataBuilder.ToString());
        }
    }
}

/// <summary>
/// SSE 事件结构
/// </summary>
public readonly record struct SseEvent(string EventType, string Data);
