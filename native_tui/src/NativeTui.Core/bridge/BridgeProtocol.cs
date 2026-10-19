namespace NativeTui.Core.Bridge;

/// <summary>读事件结果：成功 / 流终止 / 跳过（带诊断）。</summary>
public enum BridgeReadResult
{
    /// <summary>读到一条合法事件。</summary>
    Event,

    /// <summary>流终止（无更多数据）。</summary>
    Eof,

    /// <summary>行无法识别（未知 type / 坏 JSON），已跳过 — 诊断携带诱导说明。</summary>
    Skipped,
}

/// <summary>
/// bridge 协议读写器 — newline-delimited JSON over stdio（ADR 0141）：
/// 请求写入 TextWriter（每条一行 + Flush）；事件从 TextReader 行读 + 多态反序列化。
/// 空行静默跳过；未知 type / 坏 JSON 跳过并返回诊断（是什么触发 + 为什么跳过 + 合法 type 清单 + 怎么办）。
/// </summary>
public static class BridgeProtocol
{
    /// <summary>支持的事件 type 清单（诊断用，与 BridgeEvent 的 [JsonDerivedType] 一一对应）。</summary>
    private const string SupportedEventTypes =
        "turn_started, thinking_started, thinking_ended, tool_call, tool_result, token_delta, assistant_message, turn_completed, turn_interrupted, error";

    /// <summary>写一条请求（一行 JSON + LF + Flush — 协议统一 \n，不受平台 NewLine 影响）。</summary>
    public static void WriteRequest(TextWriter writer, BridgeRequest request)
    {
        writer.Write(SerializeRequest(request));
        writer.Write('\n');
        writer.Flush();
    }

    /// <summary>序列化请求为一行 JSON。</summary>
    public static string SerializeRequest(BridgeRequest request) =>
        JsonSerializer.Serialize(request, BridgeJsonContext.Default.BridgeRequest);

    /// <summary>序列化事件为一行 JSON。</summary>
    public static string SerializeEvent(BridgeEvent bridgeEvent) =>
        JsonSerializer.Serialize(bridgeEvent, BridgeJsonContext.Default.BridgeEvent);

    /// <summary>
    /// 读下一条事件：空行静默跳过；未知 type / 坏 JSON 返回 Skipped（诊断含诱导说明，调用方可记录后继续读）；
    /// 流终止返回 Eof。
    /// </summary>
    public static BridgeReadResult TryReadEvent(TextReader reader, out BridgeEvent? @event, out string? diagnostic)
    {
        while (true)
        {
            var line = reader.ReadLine();
            if (line is null)
            {
                @event = null;
                diagnostic = null;
                return BridgeReadResult.Eof;
            }

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            try
            {
                @event = JsonSerializer.Deserialize(line, BridgeJsonContext.Default.BridgeEvent);
                if (@event is not null)
                {
                    diagnostic = null;
                    return BridgeReadResult.Event;
                }
            }
            catch (JsonException ex)
            {
                @event = null;
                diagnostic = BuildDiagnostic(line, ex);
                return BridgeReadResult.Skipped;
            }
        }
    }

    /// <summary>构造诱导式诊断：是什么 type 触发 + 为什么跳过 + 合法 type 清单 + 接下来怎么做。</summary>
    private static string BuildDiagnostic(string line, JsonException ex)
    {
        var typeValue = TryGetTypeValue(line);
        return typeValue is null
            ? $"bridge 事件行不是合法 JSON（{ex.Message}），已跳过该行。合法 type：{SupportedEventTypes}。请核对引擎输出是否符合 ADR 0141 协议。"
            : $"未知事件 type '{typeValue}'，已跳过该行（不中断事件流）。支持的 type：{SupportedEventTypes}。若引擎为新版本新增事件，请同步升级 native_tui 的 BridgeEvent 契约。原始行：{line}";
    }

    /// <summary>提取行的 type 字段值（动态字段判别，仅此一处允许 JsonDocument）。</summary>
    private static string? TryGetTypeValue(string line)
    {
        try
        {
            using var doc = JsonDocument.Parse(line);
            return doc.RootElement.ValueKind == JsonValueKind.Object
                && doc.RootElement.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                ? type.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
