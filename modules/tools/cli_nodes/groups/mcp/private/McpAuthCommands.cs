// mcp 组下沉 handler — 认证配置读写（~/.jcc/mcp/auth.json，单一真相源）。
// 写类工具把一条 entry 写进 auth.json；下次任何进程启动都从文件恢复（宿主 LoadAuthStateAsync）。
// 注意：宿主长驻进程的内存字典需适配文件变更（读认证时重载）——这是宿主侧配套，不阻塞本 exe。
// mcp_connect/disconnect/list_tools/call_tool 等 13 个依赖进程内活跃连接对象（stdio 管道/SSE/WebSocket
// + ToolRegistry 注册），随进程退出销毁，物理上无法在短生命周期 exe 等价，留宿主。





namespace JoinCode.CliNode.Mcp;

internal static class McpAuthCommands {
    private static readonly FrozenDictionary<string, CliNodeHandler> Handlers =
        new Dictionary<string, CliNodeHandler>(StringComparer.Ordinal) {
            ["mcp_auth_apikey"] = SetApiKeyAsync,
            ["mcp_auth_bearer"] = SetBearerAsync,
            ["mcp_auth_basic"] = SetBasicAsync,
            ["mcp_auth_oauth2"] = SetOAuth2Async,
            ["mcp_auth_remove"] = RemoveAsync,
            ["auth_get_status"] = StatusAsync,
            ["mcp_auth_status"] = StatusAsync
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static CliNodeHandler? Find(string id) => Handlers.TryGetValue(id, out var h) ? h : null;
    public static IEnumerable<string> LocalToolIds => Handlers.Keys.OrderBy(x => x, StringComparer.Ordinal);

    private static string AuthFile => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jcc", "mcp", "auth.json");

    private static JsonArray LoadConfigs() {
        if (!File.Exists(AuthFile)) return new JsonArray();
        try {
            using var doc = JsonDocument.Parse(File.ReadAllText(AuthFile).TrimStart('\uFEFF'));
            if (doc.RootElement.TryGetProperty("AuthConfigs", out var arr) && arr.ValueKind == JsonValueKind.Array) {
                return JsonNode.Parse(arr.GetRawText()) as JsonArray ?? new JsonArray();
            }
        } catch (JsonException) { /* 损坏则视为空 */ }
        return new JsonArray();
    }

    private static void SaveConfigs(JsonArray configs) {
        Directory.CreateDirectory(Path.GetDirectoryName(AuthFile)!);
        var root = new JsonObject { ["AuthConfigs"] = configs };
        File.WriteAllText(AuthFile, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void Upsert(string authName, Action<JsonObject> fill) {
        var configs = LoadConfigs();
        var entry = configs.OfType<JsonObject>().FirstOrDefault(e => e["AuthName"]?.GetValue<string>() == authName);
        if (entry is null) { entry = new JsonObject { ["AuthName"] = authName }; configs.Add((JsonNode)entry); }
        fill(entry);
        SaveConfigs(configs);
    }

    private static Task<int> SetApiKeyAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var name = Required(o, "auth_name", "mcp_auth_apikey");
        var key = Required(o, "api_key", "mcp_auth_apikey");
        var header = Get(o, "header_name", "X-API-Key");
        Upsert(name, e => { e["AuthType"] = "ApiKey"; e["ApiKey"] = key; e["HeaderName"] = header; });
        Console.Out.WriteLine($"API Key 认证「{name}」已配置（header: {header}）");
        return Task.FromResult(0);
    }

    private static Task<int> SetBearerAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var name = Required(o, "auth_name", "mcp_auth_bearer");
        var token = Required(o, "token", "mcp_auth_bearer");
        Upsert(name, e => { e["AuthType"] = "Bearer"; e["Token"] = token; });
        Console.Out.WriteLine($"Bearer 认证「{name}」已配置");
        return Task.FromResult(0);
    }

    private static Task<int> SetBasicAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var name = Required(o, "auth_name", "mcp_auth_basic");
        var user = Required(o, "username", "mcp_auth_basic");
        var pass = Required(o, "password", "mcp_auth_basic");
        Upsert(name, e => { e["AuthType"] = "Basic"; e["Username"] = user; e["Password"] = pass; });
        Console.Out.WriteLine($"Basic 认证「{name}」已配置（用户: {user}）");
        return Task.FromResult(0);
    }

    private static Task<int> SetOAuth2Async(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var name = Required(o, "auth_name", "mcp_auth_oauth2");
        var clientId = Required(o, "client_id", "mcp_auth_oauth2");
        var secret = Required(o, "client_secret", "mcp_auth_oauth2");
        var tokenUrl = Required(o, "token_url", "mcp_auth_oauth2");
        var scopes = Get(o, "scopes", "");
        Upsert(name, e => {
            e["AuthType"] = "OAuth2"; e["ClientId"] = clientId; e["ClientSecret"] = secret; e["TokenUrl"] = tokenUrl;
            e["Scopes"] = new JsonArray(scopes.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(s => (JsonNode)JsonValue.Create(s)!).ToArray());
        });
        Console.Out.WriteLine($"OAuth2 认证「{name}」已配置（token_url: {tokenUrl}）");
        return Task.FromResult(0);
    }

    private static Task<int> RemoveAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var name = Required(o, "auth_name", "mcp_auth_remove");
        var configs = LoadConfigs();
        var target = configs.OfType<JsonObject>().FirstOrDefault(e => e["AuthName"]?.GetValue<string>() == name);
        if (target is null) { Console.Out.WriteLine($"认证配置「{name}」不存在"); return Task.FromResult(0); }
        configs.Remove(target);
        SaveConfigs(configs);
        Console.Out.WriteLine($"认证配置「{name}」已移除");
        return Task.FromResult(0);
    }

    private static Task<int> StatusAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var filter = Get(o, "auth_name", "");
        var configs = LoadConfigs().OfType<JsonObject>()
            .Where(e => filter.Length == 0 || e["AuthName"]?.GetValue<string>() == filter).ToList();
        if (configs.Count == 0) { Console.Out.WriteLine(filter.Length == 0 ? "无认证配置" : $"认证配置「{filter}」不存在"); return Task.FromResult(0); }
        foreach (var e in configs) {
            var name = e["AuthName"]?.GetValue<string>() ?? "";
            var type = e["AuthType"]?.GetValue<string>() ?? "";
            // ApiKey/Bearer/Basic 凭据在文件中 → 可准确判断；OAuth2 token 只在内存 → 报已配置
            var configured = type switch {
                "ApiKey" => !string.IsNullOrEmpty(e["ApiKey"]?.GetValue<string>()),
                "Bearer" => !string.IsNullOrEmpty(e["Token"]?.GetValue<string>()),
                "Basic" => !string.IsNullOrEmpty(e["Username"]?.GetValue<string>()),
                "OAuth2" => !string.IsNullOrEmpty(e["ClientId"]?.GetValue<string>()),
                _ => false
            };
            var note = type == "OAuth2" ? "（文件仅存配置，OAuth2 token 状态需宿主进程确认）" : "";
            Console.Out.WriteLine($"  {name}: {type} {(configured ? "已配置" : "未配置")}{note}");
        }
        return Task.FromResult(0);
    }

    private static string Required(IReadOnlyDictionary<string, string> o, string k, string tool) =>
        o.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v : throw new ArgumentException($"{tool} 缺少必填参数 --{k}。正确写法: jcc tool call mcp {tool} --{k} <值>");
    private static string Get(IReadOnlyDictionary<string, string> o, string k, string fallback) =>
        o.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;
}
