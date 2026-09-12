namespace JoinCode.Abstractions.LLM.Chat;

/// <summary>
/// ApiMessage 扩展方法 — 统一 Metadata 读写，消除硬编码字符串
/// </summary>
public static class ApiMessageExtensions
{
    /// <summary>
    /// 从 Tool 角色消息的 Metadata 中提取 ToolCallId
    /// 对齐 ChatService 写入: Metadata["ToolCallId"]
    /// </summary>
    public static string? ExtractToolCallId(this ApiMessage msg)
    {
        if (msg.Metadata is not null
            && msg.Metadata.TryGetValue(MessageMetadataKeyConstants.ToolCallId, out var idObj)
            && idObj.ValueKind == JsonValueKind.String)
        {
            return idObj.GetString();
        }

        return null;
    }

    /// <summary>
    /// 从 Tool 角色消息的 Metadata 中提取 ToolName
    /// 对齐 ChatService 写入: Metadata["ToolName"]
    /// </summary>
    public static string? ExtractToolName(this ApiMessage msg)
    {
        if (msg.Metadata is not null
            && msg.Metadata.TryGetValue(MessageMetadataKeyConstants.ToolName, out var nameObj)
            && nameObj.ValueKind == JsonValueKind.String)
        {
            return nameObj.GetString();
        }

        return null;
    }

    /// <summary>
    /// 从 Assistant 消息的 Metadata 中提取工具调用列表
    /// 对齐 ChatService 写入: Metadata["ToolCalls"] = [{Id, Name, Arguments}]
    /// </summary>
    public static List<(string Id, string Name)> ExtractToolCalls(this ApiMessage msg)
    {
        var result = new List<(string Id, string Name)>();

        if (msg.Metadata is null)
            return result;

        if (!msg.Metadata.TryGetValue(MessageMetadataKeyConstants.ToolCalls, out var toolCallsEl)
            || toolCallsEl.ValueKind != JsonValueKind.Array)
            return result;

        foreach (var tc in toolCallsEl.EnumerateArray())
        {
            if (tc.TryGetProperty("Id", out var idProp) && idProp.ValueKind == JsonValueKind.String
                && tc.TryGetProperty("Name", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
            {
                var id = idProp.GetString();
                var name = nameProp.GetString();
                if (id is not null && name is not null)
                {
                    result.Add((id, name));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// 从消息的 Metadata 中提取时间戳
    /// </summary>
    public static DateTime? ExtractTimestamp(this ApiMessage msg)
    {
        if (msg.Metadata is null)
            return null;

        if (msg.Metadata.TryGetValue(MessageMetadataKeyConstants.Timestamp, out var tsObj)
            && tsObj.ValueKind == JsonValueKind.String
            && DateTime.TryParse(tsObj.GetString(), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var ts))
        {
            return ts;
        }

        return null;
    }
}
