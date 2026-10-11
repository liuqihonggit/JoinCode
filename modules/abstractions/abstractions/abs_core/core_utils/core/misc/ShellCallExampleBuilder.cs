namespace JoinCode.Abstractions.Utils;

/// <summary>
/// Shell 调用示例构建器 — 生成跨 shell(PowerShell/Bash/Cmd)调用示例和引号提示
/// 从 ToolCallRepairService 提取,降低主类方法数和文件行数
/// </summary>
internal static class ShellCallExampleBuilder {
    /// <summary>
    /// 生成跨 shell 调用示例文本 — 帮助 AI/用户正确传递 JSON 参数
    /// <para>覆盖 PowerShell(--%)、Bash(单引号)、Cmd(转义引号)三种 shell</para>
    /// <para>若提供 schema 则按 required/properties 生成具体参数示例，否则用通用示例</para>
    /// </summary>
    internal static string BuildShellCallExamples(string toolName, ToolSchema? schema = null) {
        var exampleJson = BuildExampleJson(schema);
        var exampleKv = BuildExampleKeyValue(schema, toolName);
        return $$"""
调用示例 (JSON):
  PowerShell: jcc mcp_call {{toolName}} --% "{{exampleJson}}"
  Bash:       jcc mcp_call {{toolName}} '{{exampleJson}}'
  Cmd:        jcc mcp_call {{toolName}} "{{exampleJson}}"
调用示例 (key=value):
  All shells: jcc mcp_call {{exampleKv}}
""";
    }

    /// <summary>
    /// 根据 ToolSchema 生成示例 JSON 字符串 — 优先包含 required 参数，无 required 则包含前 3 个 properties
    /// </summary>
    private static string BuildExampleJson(ToolSchema? schema) {
        if (schema is null || schema.Properties.Count == 0)
            return "{\"key\":\"value\"}";

        var keys = schema.Required.Count > 0
            ? schema.Required
            : schema.Properties.Keys.Take(3).ToList();

        if (keys.Count == 0)
            return "{}";

        var parts = new List<string>(keys.Count);
        foreach (var key in keys) {
            if (!schema.Properties.TryGetValue(key, out var prop))
                continue;
            parts.Add($"\"{key}\":{BuildExampleValue(prop)}");
        }
        return parts.Count == 0 ? "{}" : $"{{{string.Join(",", parts)}}}";
    }

    /// <summary>
    /// 根据 ToolSchemaProperty 类型生成占位值
    /// </summary>
    private static string BuildExampleValue(ToolSchemaProperty prop) {
        if (prop.Enum is { Count: > 0 })
            return "\"" + prop.Enum[0] + "\"";
        return prop.Type switch {
            "integer" or "number" => "0",
            "boolean" => "false",
            "array" => "[]",
            "object" => "{}",
            _ => "\"<" + prop.Type + ">\"",
        };
    }

    /// <summary>
    /// 根据 ToolSchema 生成 key=value 格式示例参数 — 优先包含 required 参数
    /// </summary>
    private static string BuildExampleKeyValue(ToolSchema? schema, string toolName) {
        if (schema is null || schema.Properties.Count == 0)
            return toolName + " key=value";

        var keys = schema.Required.Count > 0
            ? schema.Required
            : schema.Properties.Keys.Take(3).ToList();

        if (keys.Count == 0)
            return toolName;

        var parts = new List<string>(keys.Count);
        foreach (var key in keys) {
            if (!schema.Properties.TryGetValue(key, out var prop))
                continue;
            parts.Add(key + "=" + BuildExampleKvValue(prop));
        }
        return parts.Count == 0 ? toolName : toolName + " " + string.Join(" ", parts);
    }

    /// <summary>
    /// 根据 ToolSchemaProperty 类型生成 key=value 占位值（不带引号）
    /// </summary>
    private static string BuildExampleKvValue(ToolSchemaProperty prop) {
        if (prop.Enum is { Count: > 0 })
            return prop.Enum[0];
        return prop.Type switch {
            "integer" or "number" => "0",
            "boolean" => "false",
            "array" => "[]",
            "object" => "{}",
            _ => "<" + prop.Type + ">",
        };
    }

    /// <summary>
    /// 检测"引号被 shell 剥落"特征并返回修正写法提示 — 以 { 开头、有冒号、但无双引号
    /// <para>返回 null 表示未检测到该特征(不提示)</para>
    /// </summary>
    internal static string? BuildShellQuoteHint(string json) {
        if (json.Length > 0 && json[0] == '{' && json.Contains(':') && !json.Contains('"')) {
            return """
提示: 输入看起来像被 shell 剥掉了引号。
  PowerShell: 用 --% 停止解析,或用 \" 转义双引号
  示例: jcc mcp_call <tool> --% "{\"key\":\"value\"}"
""";
        }
        return null;
    }
}