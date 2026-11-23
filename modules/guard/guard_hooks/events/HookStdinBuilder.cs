namespace Core.Hooks.Events;

/// <summary>
/// Hook stdin JSON 构造器（TASK071-D1+D2+D3 — 对齐基准 hooks.ts createBaseHookInput + stdin.write）：
/// 构造 snake_case 字段名的 JSON 字符串写入 hook 进程 stdin。
/// 基础字段：session_id/transcript_path/cwd/hook_event_name（对齐基准 createBaseHookInput）。
/// 事件特定字段：tool_name/tool_input/tool_use_id（ToolUse 事件）/ source（Session 事件）等。
/// </summary>
public static class HookStdinBuilder {
    /// <summary>
    /// 构造 hook stdin JSON 字符串（snake_case 字段名 + 基础字段 + 事件特定字段）。
    /// </summary>
    /// <param name="input">钩子输入数据（含 Event/Payload/ToolName/ToolUseId/SessionId）</param>
    /// <param name="transcriptPath">可选的 transcript 文件路径（对齐基准 transcript_path）</param>
    /// <returns>snake_case JSON 字符串</returns>
    public static string BuildStdinJson(HookInput input, string? transcriptPath = null) {
        var dict = new Dictionary<string, JsonElement> {
            ["session_id"] = JsonElementHelper.FromString(input.SessionId ?? string.Empty),
            ["transcript_path"] = JsonElementHelper.FromString(transcriptPath ?? string.Empty),
            ["cwd"] = JsonElementHelper.FromString(Environment.CurrentDirectory),
            ["hook_event_name"] = JsonElementHelper.FromString(input.EventName)
        };

        foreach (var (key, value) in input.Payload) {
            dict[ToSnakeCase(key)] = value;
        }

        return JsonSerializer.Serialize(dict, HooksJsonContext.Default.DictionaryStringJsonElement);
    }

    /// <summary>
    /// camelCase → snake_case 转换（如 "toolName" → "tool_name"、"toolUseId" → "tool_use_id"）。
    /// </summary>
    private static string ToSnakeCase(string camelCase) {
        if (camelCase.Length == 0) {
            return camelCase;
        }

        var sb = new StringBuilder(camelCase.Length + 4);
        sb.Append(char.ToLowerInvariant(camelCase[0]));
        for (var i = 1; i < camelCase.Length; i++) {
            if (char.IsUpper(camelCase[i])) {
                sb.Append('_');
                sb.Append(char.ToLowerInvariant(camelCase[i]));
            } else {
                sb.Append(camelCase[i]);
            }
        }

        return sb.ToString();
    }
}
