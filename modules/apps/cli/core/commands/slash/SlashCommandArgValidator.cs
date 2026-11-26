namespace JoinCode.CliCommands;

/// <summary>
/// 斜杠命令参数的结构化校验 — 真相来自 <c>[ChatCommandArg]</c> 生成的
/// <see cref="JoinCode.Abstractions.Interfaces.ISlashCommandSchemaCatalog"/>：
/// 授权层（slash.yaml）只管参数名白名单，这里补 required / enum / type。
/// <para>为什么不在策略 YAML 里重复类型：权限与结构是两套真相，
/// 类型/必填/枚举已在特性里声明并经 help 新鲜度对账，重复登记必然漂移。</para>
/// </summary>
internal static class SlashCommandArgValidator {
    /// <summary>
    /// 校验 slash_call 的参数 JSON；通过返回 null，失败返回带正确写法的单行错误。
    /// <para>命令未声明结构化参数（schema 为 null）时直接放行，保持渐进式兼容。</para>
    /// </summary>
    /// <param name="commandName">canonical 命令名（不含斜杠）。</param>
    /// <param name="argsJson">参数 JSON（对象形态）。</param>
    /// <param name="schema">命令参数 schema；null 表示未声明。</param>
    /// <returns>错误描述；通过返回 null。</returns>
    internal static string? Validate(string commandName, string argsJson, JoinCode.Abstractions.Tools.ToolSchema? schema) {
        if (schema is null || schema.Properties.Count == 0) return null;

        JsonDocument document;
        try {
            document = JsonDocument.Parse(argsJson);
        } catch (JsonException) {
            // 格式层（ResolveArgsJsonAsync）已经拦过非对象/坏 JSON，这里不重复报。
            return null;
        }

        using (document) {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            var provided = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject()) provided[property.Name] = property.Value;

            var missing = schema.Required.Where(name => !provided.ContainsKey(name)).ToArray();
            if (missing.Length > 0) {
                return Failure(
                    commandName,
                    $"缺少必填参数: {string.Join(", ", missing.Select(name => "--" + name))}",
                    $"[ChatCommandArg] 把 {string.Join("/", missing)} 标记为 Required",
                    commandName);
            }

            foreach (var (name, property) in schema.Properties) {
                if (!provided.TryGetValue(name, out var value)) continue;
                var typeError = CheckType(property.Type, value);
                if (typeError is not null) {
                    return Failure(
                        commandName,
                        $"参数 '{name}' {typeError}",
                        $"schema 声明类型为 {property.Type}",
                        commandName);
                }
                if (property.Enum is { Count: > 0 } allowed && !MatchesEnum(value, allowed)) {
                    return Failure(
                        commandName,
                        $"参数 '{name}' 取值 '{Describe(value)}' 不在允许集合",
                        $"允许: {string.Join(", ", allowed)}",
                        commandName);
                }
            }
            return null;
        }
    }

    private static string? CheckType(string declaredType, JsonElement value) => declaredType switch {
        "string" => null,
        "number" => value.ValueKind == JsonValueKind.Number
            || (value.ValueKind == JsonValueKind.String
                && double.TryParse(value.GetString(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out _))
            ? null
            : $"需要 number，实际为 {Describe(value)}",
        "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False
            || (value.ValueKind == JsonValueKind.String && IsBooleanText(value.GetString()))
            ? null
            : $"需要 boolean（true/false/1/0），实际为 {Describe(value)}",
        "array" => value.ValueKind == JsonValueKind.Array || IsJsonOfKind(value, JsonValueKind.Array)
            ? null
            : $"需要 array（JSON 数组），实际为 {Describe(value)}",
        "object" => value.ValueKind == JsonValueKind.Object || IsJsonOfKind(value, JsonValueKind.Object)
            ? null
            : $"需要 object（JSON 对象），实际为 {Describe(value)}",
        _ => null
    };

    private static bool IsBooleanText(string? text) =>
        text is not null
        && (bool.TryParse(text, out _)
            || string.Equals(text, "1", StringComparison.Ordinal)
            || string.Equals(text, "0", StringComparison.Ordinal));

    private static bool IsJsonOfKind(JsonElement value, JsonValueKind kind) {
        if (value.ValueKind != JsonValueKind.String) return false;
        try {
            using var nested = JsonDocument.Parse(value.GetString() ?? "");
            return nested.RootElement.ValueKind == kind;
        } catch (JsonException) {
            return false;
        }
    }

    private static bool MatchesEnum(JsonElement value, List<string> allowed) =>
        allowed.Contains(Describe(value), StringComparer.Ordinal);

    private static string Describe(JsonElement value) => value.ValueKind switch {
        JsonValueKind.String => value.GetString() ?? "",
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "null",
        JsonValueKind.Number => value.GetRawText(),
        JsonValueKind.Array => "array",
        JsonValueKind.Object => "object",
        _ => value.GetRawText()
    };

    private static string Failure(string commandName, string reason, string detail, string schemaCommand) =>
        $"[SLASH-SCHEMA] denied | command: /{commandName} | layer: Slash.Schema | reason: {reason}（{detail}）"
        + $" | 正确写法: jcc slash_call {commandName} 按 schema 传参，或 jcc slash_schema {schemaCommand} 查看参数表";
}
