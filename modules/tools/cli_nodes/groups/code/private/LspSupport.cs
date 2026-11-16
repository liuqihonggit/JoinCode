// LSP 配置与 JSON 取值助手 —— 配置读 ~/.jcc/lsp-servers.json（与宿主 LspConfigLoader 同一文件）。
using System.Text.Json;
using System.Text.Json.Nodes;

namespace JoinCode.CliNode.Code;

/// <summary>一台 LSP 服务器的启动配置（字段取自 lsp-servers.json）。</summary>
internal sealed record LspServerEntry(
    string ServerId,
    string Command,
    string[] Arguments,
    string? WorkingDirectory,
    string[] Extensions,
    string LanguageId,
    int StartupTimeoutSeconds,
    Dictionary<string, string> Environment) {

    public static LspServerEntry From(JsonNode? node) => new(
        LspJson.Str(node?["serverId"]),
        LspJson.Str(node?["command"]),
        LspJson.StringArray(node?["arguments"]),
        LspJson.Str(node?["workingDirectory"]) is { Length: > 0 } workingDirectory ? workingDirectory : null,
        LspJson.StringArray(node?["fileExtensions"]),
        LspJson.Str(node?["languageId"]) is { Length: > 0 } languageId ? languageId : "plaintext",
        LspJson.Int(node?["startupTimeoutSeconds"], 30),
        LspJson.StringMap(node?["environmentVariables"]));

    public bool Enabled { get; init; } = true;
}

/// <summary>lsp-servers.json 读取 — 只读，不写回（写配置仍归宿主）。</summary>
internal static class LspConfig {
    public static string ConfigPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jcc", "lsp-servers.json");

    public static JsonArray LoadEntries() {
        var path = ConfigPath;
        if (!File.Exists(path)) throw new InvalidOperationException($"LSP 配置不存在: {path}（宿主首次启动会自动生成一份默认配置）");
        try {
            return JsonNode.Parse(File.ReadAllText(path).TrimStart('\uFEFF')) as JsonArray ?? [];
        } catch (JsonException ex) {
            throw new InvalidOperationException($"LSP 配置不是合法 JSON: {path}（{ex.Message}）");
        }
    }

    public static IEnumerable<LspServerEntry> Servers() {
        foreach (var node in LoadEntries()) {
            var enabled = node?["enabled"] is JsonValue value && value.TryGetValue<bool>(out var flag) ? flag : true;
            yield return LspServerEntry.From(node) with { Enabled = enabled };
        }
    }

    public static LspServerEntry? ForFile(string filePath) {
        var extension = Path.GetExtension(filePath);
        return Servers().FirstOrDefault(server => server.Enabled && server.Extensions.Contains(extension, StringComparer.OrdinalIgnoreCase));
    }

    public static LspServerEntry? ByName(string serverName) =>
        Servers().FirstOrDefault(server => server.Enabled && string.Equals(server.ServerId, serverName, StringComparison.OrdinalIgnoreCase));

    public static LspServerEntry FirstEnabled() =>
        Servers().FirstOrDefault(server => server.Enabled)
        ?? throw new InvalidOperationException($"配置 {ConfigPath} 里没有启用的 LSP 服务器");
}

/// <summary>JsonNode 取值助手 — 缺字段/类型不符一律给默认值，不抛。</summary>
internal static class LspJson {
    public static string Str(JsonNode? node) {
        if (node is not JsonValue value) return "";
        return value.TryGetValue<string>(out var text) ? text : value.ToJsonString();
    }

    public static int Int(JsonNode? node, int fallback = 0) =>
        node is JsonValue value && value.TryGetValue<int>(out var number) ? number : fallback;

    public static string[] StringArray(JsonNode? node) =>
        node is JsonArray array ? [.. array.Select(Str).Where(s => s.Length > 0)] : [];

    public static Dictionary<string, string> StringMap(JsonNode? node) {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (node is JsonObject obj) {
            foreach (var (key, value) in obj) map[key] = Str(value);
        }
        return map;
    }
}
