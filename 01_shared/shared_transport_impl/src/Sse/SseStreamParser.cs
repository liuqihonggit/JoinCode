namespace JoinCode.Transport;

/// <summary>
/// SSE 事件流解析器 — 从 Stream 中解析 SSE 事件
/// </summary>
/// <remarks>
/// 对齐 SSE 规范: event:/data:/id:/空行分隔，多行 data 用换行拼接。
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
        string? eventId = null;

        while (!ct.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) break;

            if (string.IsNullOrEmpty(line))
            {
                if (dataBuilder.Length > 0)
                {
                    yield return new SseEvent(eventType, dataBuilder.ToString(), eventId);
                    dataBuilder.Clear();
                    eventType = string.Empty;
                    eventId = null;
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
            else if (line.StartsWith("id:", StringComparison.Ordinal))
            {
                eventId = line.AsSpan(3).Trim().ToString();
            }
        }

        if (dataBuilder.Length > 0)
        {
            yield return new SseEvent(eventType, dataBuilder.ToString(), eventId);
        }
    }
}

/// <summary>
/// SSE 事件结构
/// </summary>
public readonly record struct SseEvent(string EventType, string Data, string? Id = null);
