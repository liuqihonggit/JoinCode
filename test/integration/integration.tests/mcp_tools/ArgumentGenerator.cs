namespace Integration.Tests.McpTools;

/// <summary>
/// 工具参数生成器 — 生成默认值参数 + 缺失参数场景
/// </summary>
public static class ArgumentGenerator {
    /// <summary>
    /// 生成默认值参数 — Required 用 schema 默认值，Optional 用 null
    /// </summary>
    public static Dictionary<string, JsonElement> GenerateDefaultArguments(ToolInfo tool) {
        var args = new Dictionary<string, JsonElement>();
        if (tool.InputSchema?.Required == null) return args;

        foreach (var reqParam in tool.InputSchema.Required) {
            if (!tool.InputSchema.Properties.TryGetValue(reqParam, out var prop)) continue;
            args[reqParam] = GenerateDefaultValue(reqParam, prop);
        }
        return args;
    }

    /// <summary>
    /// 生成缺失参数场景 — 逐个移除每个 Required 参数
    /// </summary>
    public static IEnumerable<(string MissingParam, Dictionary<string, JsonElement> Args)> GenerateMissingParamScenarios(ToolInfo tool) {
        if (tool.InputSchema?.Required == null) yield break;

        var fullArgs = GenerateDefaultArguments(tool);
        foreach (var reqParam in tool.InputSchema.Required) {
            var missingArgs = new Dictionary<string, JsonElement>(fullArgs);
            missingArgs.Remove(reqParam);
            yield return (reqParam, missingArgs);
        }
    }

    private static JsonElement GenerateDefaultValue(string paramName, ToolSchemaProperty prop) {
        if (!string.IsNullOrEmpty(prop.Default)) {
            return JsonDocument.Parse(prop.Default).RootElement.Clone();
        }

        return prop.Type switch {
            "string" => GenerateStringDefault(paramName, prop),
            "integer" => JsonDocument.Parse("1").RootElement.Clone(),
            "number" => JsonDocument.Parse("1.0").RootElement.Clone(),
            "boolean" => JsonDocument.Parse("false").RootElement.Clone(),
            "array" => JsonDocument.Parse("[]").RootElement.Clone(),
            "object" => JsonDocument.Parse("{}").RootElement.Clone(),
            _ => JsonDocument.Parse("\"test\"").RootElement.Clone()
        };
    }

    private static JsonElement GenerateStringDefault(string paramName, ToolSchemaProperty prop) {
        if (prop.Enum is { Count: > 0 }) {
            return JsonDocument.Parse($"\"{prop.Enum[0]}\"").RootElement.Clone();
        }

        var value = paramName switch {
            "file_path" or "path" => "test.txt",
            "directory_path" => ".",
            "command" => "echo hello",
            "pattern" => "*.txt",
            "query" => "test",
            "url" => "http://localhost",
            "prompt" => "test",
            "code" => "var x = 1;",
            "expression" => "1+1",
            "schema_name" => "test_schema",
            "schema_json" => "{}",
            "action" => "list",
            "target" => "test",
            "confirm" => "yes",
            "agent_pattern" => "*",
            "agent_name" => "test",
            "cassette_name" => "test",
            "client_id" => "test",
            "tool_name" => "test",
            "resource_uri" => "test://resource",
            "auth_id" => "test",
            "api_key" => "test-key",
            "token" => "test-token",
            "username" => "test",
            "password" => "test",
            "refresh_token" => "test",
            "client_secret" => "test",
            "auth_url" => "http://localhost/auth",
            "token_url" => "http://localhost/token",
            "pr_number" or "issue_number" or "run_id" => "1",
            "branch" => "main",
            "owner" => "test",
            "repo" => "test",
            "tag" => "v1.0.0",
            _ => "test"
        };
        return JsonDocument.Parse($"\"{value}\"").RootElement.Clone();
    }
}
