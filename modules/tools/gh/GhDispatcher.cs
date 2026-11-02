namespace JoinCode.Gh;

internal static class GhDispatcher {
    private static readonly Dictionary<string, MethodInfo> s_toolMap = BuildToolMap();
    private static readonly GitHubToolHandlers s_handler = GhHost.BuildHandler();

    private static Dictionary<string, MethodInfo> BuildToolMap() {
        var map = new Dictionary<string, MethodInfo>(StringComparer.Ordinal);
        foreach (var method in typeof(GitHubToolHandlers).GetMethods(BindingFlags.Public | BindingFlags.Instance)) {
            var attr = method.GetCustomAttribute<McpToolAttribute>();
            if (attr is null) continue;
            var name = attr.Name;
            if (!string.IsNullOrEmpty(name)) map[name] = method;
        }
        return map;
    }

    internal static IReadOnlyList<string> GetToolNames() => s_toolMap.Keys.OrderBy(n => n).ToList();
    internal static bool ContainsTool(string toolName) => s_toolMap.ContainsKey(toolName);

    internal static List<GhParam> GetParams(string toolName) {
        if (!s_toolMap.TryGetValue(toolName, out var method)) return [];
        var result = new List<GhParam>();
        foreach (var p in method.GetParameters()) {
            if (p.GetCustomAttribute<McpToolOptionsAttribute>() is not null) {
                foreach (var prop in p.ParameterType.GetProperties()) {
                    if (prop.GetCustomAttribute<McpToolParameterAttribute>() is null) continue;
                    AddParam(result, prop.Name, prop.PropertyType, prop.GetCustomAttribute<McpToolParameterAttribute>(), true);
                }
                continue;
            }
            if (p.ParameterType == typeof(CancellationToken)) continue;
            AddParam(result, p.Name ?? "?", p.ParameterType, p.GetCustomAttribute<McpToolParameterAttribute>(), p.HasDefaultValue);
        }
        return result;
    }

    private static void AddParam(List<GhParam> result, string name, Type type, McpToolParameterAttribute? paramAttr, bool hasDefaultValue) {
        var isRequired = (paramAttr?.Required ?? true) && !hasDefaultValue;
        var typeHint = type switch {
            _ when type == typeof(string) => "string",
            _ when type == typeof(int) || type == typeof(int?) => "integer",
            _ when type == typeof(bool) || type == typeof(bool?) => "boolean",
            _ => "string"
        };
        var isBoolean = typeHint == "boolean";
        result.Add(new GhParam(name, isRequired, isBoolean, typeHint));
    }

    internal static async Task<ToolResult?> InvokeAsync(string toolName, Dictionary<string, string> args, CancellationToken ct) {
        if (!s_toolMap.TryGetValue(toolName, out var method)) return null;
        var parameters = method.GetParameters();
        var argValues = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++) {
            var p = parameters[i];
            if (p.ParameterType == typeof(CancellationToken)) { argValues[i] = ct; continue; }
            if (p.GetCustomAttribute<McpToolOptionsAttribute>() is not null) {
                argValues[i] = BuildCommonOptions(args);
                continue;
            }
            var paramAttr = p.GetCustomAttribute<McpToolParameterAttribute>();
            var name = p.Name ?? "?";
            if (args.TryGetValue(name, out var value)) {
                argValues[i] = ConvertValue(value, p.ParameterType);
            } else if (p.HasDefaultValue) {
                argValues[i] = p.DefaultValue;
            } else {
                argValues[i] = p.ParameterType == typeof(string) ? null : null;
            }
        }
        var task = (Task<ToolResult>?)method.Invoke(s_handler, argValues);
        if (task is null) return null;
        return await task.ConfigureAwait(false);
    }

    private static GitHubCommonOptions BuildCommonOptions(Dictionary<string, string> args) {
        string? Get(string key) => args.TryGetValue(key, out var v) ? v : null;
        int? GetInt(string key) => Get(key) is { } s && int.TryParse(s, out var i) ? i : null;
        return new GitHubCommonOptions {
            Repo = Get("repo"), WorkingDir = Get("working_dir"),
            Verbosity = GetInt("verbosity"), JsonFields = Get("json_fields"),
            Jq = Get("jq"), SkipLines = GetInt("skip_lines"), StartLine = GetInt("start_line"),
        };
    }

    private static object? ConvertValue(string value, Type targetType) {
        if (targetType == typeof(string)) return value;
        if (targetType == typeof(int)) return int.TryParse(value, out var i) ? i : 0;
        if (targetType == typeof(int?)) return int.TryParse(value, out var i2) ? i2 : null;
        if (targetType == typeof(bool)) return ParseBool(value);
        if (targetType == typeof(bool?)) return ParseBool(value);
        return value;
    }

    private static bool? ParseBool(string value) {
        if (string.IsNullOrEmpty(value)) return null;
        return value.ToLowerInvariant() switch {
            "true" or "1" or "yes" or "on" => true,
            "false" or "0" or "no" or "off" => false,
            _ => bool.TryParse(value, out var b) ? b : null
        };
    }
}
