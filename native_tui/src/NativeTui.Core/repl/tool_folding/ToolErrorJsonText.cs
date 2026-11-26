using System.Text;
using System.Text.Json;

namespace NativeTui.Core.Repl;

/// <summary>
/// 工具错误 JSON 展示归一（TASK108 P13）— 引擎把工具错误统一序列化为
/// <c>{"type":…,"error_code":…,"tool":…,"message":…,"details":[…],"suggestions":[…]}</c>
/// 放进 <c>ToolResult.Content[0].Text</c>（见 <c>ToolErrorJson</c>：AI 解析结构化错误用，**数据面不改**）。
/// TUI 直接透传会把机读结构摊给用户：真机
/// <c>⎿ Error: {"type":"permission_denied","tool":"bash","message":"…"}</c>。
/// 展示层归一：取 <c>message</c> 作正文，<c>details</c> 折成 `（k: v, …）`，其余字段丢弃。
///
/// 误伤保护（普通工具输出不得改写）：三个条件同时成立才归一 —
/// 1. 文本以 `Error: ` 前缀开头（仅错误摘要带此前缀，`cat x.json` 之类不带）；
/// 2. 前缀之后是 JSON object；
/// 3. 存在非空字符串 `message` 字段。
/// 解析失败/条件不满足一律原样返回。
/// </summary>
public static class ToolErrorJsonText
{
    /// <summary>错误前缀（与 <see cref="ToolErrorText.ErrorPrefix"/> 同源语义）。</summary>
    public const string ErrorPrefix = "Error: ";

    /// <summary>details 折入上限（超出丢弃 — 摘要行保持单行可读，原文仍在 Summary）。</summary>
    private const int MaxDetails = 3;

    /// <summary>归一为人类可读文本（非错误 JSON 原样返回）。</summary>
    public static string Humanize(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text ?? string.Empty;
        }

        var trimmed = text.Trim();
        if (!trimmed.StartsWith(ErrorPrefix, StringComparison.Ordinal))
        {
            return text;
        }

        var payload = trimmed[ErrorPrefix.Length..].TrimStart();
        if (payload.Length < 2 || payload[0] != '{')
        {
            return text;
        }

        return TryRead(payload, out var message, out var details)
            ? details.Length == 0
                ? ErrorPrefix + message
                : $"{ErrorPrefix}{message}（{details}）"
            : text;
    }

    /// <summary>从 JSON 载荷提取 message 与 details 摘要（失败返回 false — 调用方原样返回）。</summary>
    private static bool TryRead(string payload, out string message, out string details)
    {
        message = string.Empty;
        details = string.Empty;
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryGetString(root, "message", out var rawMessage))
            {
                return false;
            }

            message = rawMessage.Trim();
            if (message.Length == 0)
            {
                return false;
            }

            details = ReadDetails(root);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    /// <summary>读取字符串字段（大小写不敏感 — 兼容 PascalCase 序列化形态）。</summary>
    private static bool TryGetString(JsonElement element, string name, out string value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                || property.Value.ValueKind != JsonValueKind.String)
            {
                continue;
            }

            value = property.Value.GetString() ?? string.Empty;
            return true;
        }

        value = string.Empty;
        return false;
    }

    /// <summary>details 数组 → `k: v, k2: v2`（最多 <see cref="MaxDetails"/> 项）。</summary>
    private static string ReadDetails(JsonElement root)
    {
        if (!root.TryGetProperty("details", out var details)
            || details.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();
        var count = 0;
        foreach (var entry in details.EnumerateArray())
        {
            if (count >= MaxDetails)
            {
                break;
            }

            if (entry.ValueKind != JsonValueKind.Object
                || !TryGetString(entry, "key", out var key)
                || !TryGetString(entry, "value", out var value)
                || key.Length == 0)
            {
                continue;
            }

            if (builder.Length > 0)
            {
                builder.Append(", ");
            }

            builder.Append(key).Append(": ").Append(value);
            count++;
        }

        return builder.ToString();
    }
}
