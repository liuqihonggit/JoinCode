namespace MockServer.Core;

public static class TokenEstimator
{
    public static int EstimateFromMessages(JsonElement request)
    {
        var totalChars = 0;

        if (request.TryGetProperty("messages", out var messages))
        {
            foreach (var msg in messages.EnumerateArray())
            {
                if (msg.TryGetProperty("content", out var content))
                {
                    totalChars += content.ValueKind == JsonValueKind.String
                        ? content.GetString()?.Length ?? 0
                        : content.GetRawText().Length;
                }
            }
        }

        if (request.TryGetProperty("system", out var system))
        {
            if (system.ValueKind == JsonValueKind.Array)
            {
                foreach (var block in system.EnumerateArray())
                {
                    if (block.TryGetProperty("text", out var text))
                        totalChars += text.GetString()?.Length ?? 0;
                }
            }
            else if (system.ValueKind == JsonValueKind.String)
            {
                totalChars += system.GetString()?.Length ?? 0;
            }
        }

        return totalChars / 4;
    }

    public static string ExtractSystemPrefix(JsonElement request)
    {
        if (request.TryGetProperty("system", out var system))
        {
            if (system.ValueKind == JsonValueKind.Array && system.GetArrayLength() > 0)
            {
                var firstBlock = system[0];
                if (firstBlock.TryGetProperty("text", out var text))
                    return text.GetString() ?? "";
            }
            else if (system.ValueKind == JsonValueKind.String)
            {
                return system.GetString() ?? "";
            }
        }

        if (request.TryGetProperty("messages", out var messages))
        {
            foreach (var msg in messages.EnumerateArray())
            {
                if (msg.TryGetProperty("role", out var role) &&
                    role.GetString() == "system" &&
                    msg.TryGetProperty("content", out var content))
                {
                    return content.GetString() ?? "";
                }
            }
        }

        return "";
    }

    /// <summary>
    /// 提取完整对话前缀 — 用于 PrefixCacheSimulator 的缓存键
    ///
    /// 真实 LLM Prefix Cache 基于"完整对话前缀"匹配:
    /// - 多轮对话中 system prompt 保持不变, messages 数组逐步增长
    /// - Turn 1 prefix = system + user1
    /// - Turn 2 prefix = system + user1 + assistant1 + user2 (以 Turn 1 prefix 为前缀)
    /// - 因此 Turn 2 部分命中 (Turn 1 部分已缓存)
    ///
    /// 与 ExtractSystemPrefix 的区别:
    /// - ExtractSystemPrefix 只提取 system prompt, 无法区分多轮对话 (所有轮次 system 相同)
    /// - ExtractConversationPrefix 提取 system + 所有消息内容, 正确模拟前缀增长
    ///
    /// 支持两种格式:
    /// - OpenAI: messages 数组中 role=system 的消息
    /// - Anthropic: 顶层 system 字段 (字符串或 content blocks 数组) + messages 数组
    ///
    /// 使用 '\x00' 分隔符避免消息边界处的误匹配
    /// </summary>
    public static string ExtractConversationPrefix(JsonElement request)
    {
        var sb = new StringBuilder();

        // 1. 提取 system prompt (Anthropic 顶层 system 字段 或 OpenAI messages 中 role=system)
        var systemText = ExtractSystemText(request);
        if (!string.IsNullOrEmpty(systemText))
        {
            sb.Append(systemText);
            sb.Append('\x00');
        }

        // 2. 追加所有消息内容 (按顺序, 跳过 system 角色消息避免重复)
        if (request.TryGetProperty("messages", out var messages))
        {
            foreach (var msg in messages.EnumerateArray())
            {
                var role = msg.TryGetProperty("role", out var r) ? r.GetString() ?? "" : "";
                if (role == "system") continue; // system 已在前面处理

                var contentText = ExtractContentText(msg.TryGetProperty("content", out var c) ? c : default);
                if (!string.IsNullOrEmpty(contentText))
                {
                    sb.Append(role);
                    sb.Append('\x01');
                    sb.Append(contentText);
                    sb.Append('\x00');
                }
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// 提取 system prompt 文本 — 支持 Anthropic 顶层 system 字段和 OpenAI messages 中 system 角色
    /// </summary>
    private static string ExtractSystemText(JsonElement request)
    {
        if (request.TryGetProperty("system", out var system))
        {
            if (system.ValueKind == JsonValueKind.Array)
            {
                var sb = new StringBuilder();
                foreach (var block in system.EnumerateArray())
                {
                    if (block.TryGetProperty("text", out var text))
                    {
                        sb.Append(text.GetString() ?? "");
                    }
                }
                return sb.ToString();
            }
            if (system.ValueKind == JsonValueKind.String)
            {
                return system.GetString() ?? "";
            }
        }

        if (request.TryGetProperty("messages", out var messages))
        {
            foreach (var msg in messages.EnumerateArray())
            {
                if (msg.TryGetProperty("role", out var role) &&
                    role.GetString() == "system" &&
                    msg.TryGetProperty("content", out var content))
                {
                    return ExtractContentText(content);
                }
            }
        }

        return "";
    }

    /// <summary>
    /// 提取消息 content 文本 — 支持字符串和 content blocks 数组 (Anthropic 格式)
    /// </summary>
    private static string ExtractContentText(JsonElement content)
    {
        if (content.ValueKind == JsonValueKind.String)
        {
            return content.GetString() ?? "";
        }

        if (content.ValueKind == JsonValueKind.Array)
        {
            var sb = new StringBuilder();
            foreach (var block in content.EnumerateArray())
            {
                if (block.TryGetProperty("text", out var text))
                {
                    sb.Append(text.GetString() ?? "");
                }
                else if (block.TryGetProperty("content", out var nestedContent))
                {
                    sb.Append(ExtractContentText(nestedContent));
                }
            }
            return sb.ToString();
        }

        return "";
    }
}
