// code 组 lsp_* 下沉 —— exe 自持语言服务器进程（JSON-RPC over stdio），不依赖宿主常驻实例。
// 只用 BCL：Process 起服务器、手写 Content-Length 分帧、JsonNode 收发。
// 配置读 ~/.jcc/lsp-servers.json（与宿主 LspConfigLoader 同一文件，不另立真相源）。




namespace JoinCode.CliNode.Code;

internal static partial class CodeCommands {
    private const int MaxLspFileSizeBytes = 10_000_000;

    // ==================== lsp 工具入口 ====================

    private static Task<int> LspGotoDefinitionAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) =>
        LspLocationToolAsync(o, ct, "textDocument/definition", "定义");

    private static Task<int> LspGotoImplementationAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) =>
        LspLocationToolAsync(o, ct, "textDocument/implementation", "实现");

    private static Task<int> LspFindReferencesAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) =>
        LspLocationToolAsync(o, ct, "textDocument/references", "引用");

    private static Task<int> LspHoverAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) =>
        LspPositionToolAsync(o, ct, "textDocument/hover", PrintHover);

    private static Task<int> LspCompletionAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) =>
        LspPositionToolAsync(o, ct, "textDocument/completion", PrintCompletion);

    private static Task<int> LspDocumentSymbolsAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var file = ResolveFile(Required(o, "file_path", "lsp_document_symbols"));
        return RunWithServerAsync(o, file, async (conn, languageId, token) => {
            await conn.OpenFileAsync(file, languageId, token).ConfigureAwait(false);
            var result = await conn.RequestAsync("textDocument/documentSymbol", new JsonObject {
                ["textDocument"] = new JsonObject { ["uri"] = ToFileUri(file) }
            }, token).ConfigureAwait(false);

            var sb = new StringBuilder();
            if (result is JsonArray { Count: > 0 } symbols) {
                sb.AppendLine($"文档符号 {symbols.Count} 个");
                sb.AppendLine();
                PrintDocumentSymbols(sb, symbols, 0);
            } else {
                sb.AppendLine("未找到文档符号（服务器可能还未完成索引，或该文件无符号）");
            }
            Console.Out.Write(sb.ToString());
            return 0;
        }, ct);
    }

    private static Task<int> LspWorkspaceSymbolAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var query = Required(o, "query", "lsp_workspace_symbol");
        var workspace = Nullable(o, "workspace_path");
        var serverName = Nullable(o, "server_name");
        var root = ResolveWorkspace(workspace ?? Directory.GetCurrentDirectory());
        return RunWithServerByNameAsync(serverName, root, async (conn, languageId, token) => {
            var result = await conn.RequestAsync("workspace/symbol", new JsonObject { ["query"] = query }, token).ConfigureAwait(false);

            var sb = new StringBuilder();
            if (result is JsonArray { Count: > 0 } symbols) {
                sb.AppendLine($"workspace/symbol '{query}' 命中 {symbols.Count} 个");
                sb.AppendLine();
                foreach (var node in symbols.Take(100)) {
                    var line = LspJson.Int(node?["location"]?["range"]?["start"]?["line"]) + 1;
                    sb.AppendLine($"  {LspJson.Str(node?["name"])} [{KindName(LspJson.Int(node?["kind"]))}] {UriToPath(LspJson.Str(node?["location"]?["uri"]))}:{line.ToString(CultureInfo.InvariantCulture)}");
                }
            } else {
                sb.AppendLine($"workspace/symbol 无命中: {query}");
            }
            Console.Out.Write(sb.ToString());
            return 0;
        }, ct);
    }

    private static Task<int> LspPrepareCallHierarchyAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) =>
        LspPositionToolAsync(o, ct, "textDocument/prepareCallHierarchy", (sb, result) => {
            if (result is not JsonArray { Count: > 0 } items) {
                sb.AppendLine("未能解析出调用层级条目（服务器可能还未索引该位置）");
                return;
            }
            sb.AppendLine($"调用层级条目 {items.Count} 个");
            foreach (var item in items) {
                var line = LspJson.Int(item?["range"]?["start"]?["line"]) + 1;
                sb.AppendLine($"  {LspJson.Str(item?["name"])} [{KindName(LspJson.Int(item?["kind"]))}] {UriToPath(LspJson.Str(item?["uri"]))}:{line.ToString(CultureInfo.InvariantCulture)}");
            }
        });

    private static Task<int> LspIncomingCallsAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) =>
        LspCallHierarchyToolAsync(o, ct, "callHierarchy/incomingCalls", "调用方");

    private static Task<int> LspOutgoingCallsAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) =>
        LspCallHierarchyToolAsync(o, ct, "callHierarchy/outgoingCalls", "被调用方");

    // ==================== 通用执行骨架 ====================

    /// <summary>按文件扩展名挑服务器，跑一段请求逻辑，退出时关服务器。</summary>
    private static async Task<int> RunWithServerAsync(IReadOnlyDictionary<string, string> o, string filePath, Func<LspConnection, string, CancellationToken, Task<int>> body, CancellationToken ct) {
        var entry = LspConfig.ForFile(filePath)
            ?? throw new InvalidOperationException($"没有为 '{Path.GetExtension(filePath)}' 配置可用的 LSP 服务器（配置: {LspConfig.ConfigPath}）。在配置里加一条 command/fileExtensions 后重试。");
        var root = ResolveWorkspace(ResolveWorkspaceHint(o) ?? Path.GetDirectoryName(filePath) ?? Directory.GetCurrentDirectory());
        return await RunAsync(entry, root, body, ct).ConfigureAwait(false);
    }

    private static async Task<int> RunWithServerByNameAsync(string? serverName, string root, Func<LspConnection, string, CancellationToken, Task<int>> body, CancellationToken ct) {
        var entry = string.IsNullOrWhiteSpace(serverName)
            ? LspConfig.FirstEnabled()
            : LspConfig.ByName(serverName) ?? throw new InvalidOperationException($"配置 {LspConfig.ConfigPath} 里没有名为 '{serverName}' 的启用服务器。");
        return await RunAsync(entry, root, body, ct).ConfigureAwait(false);
    }

    private static async Task<int> RunAsync(LspServerEntry entry, string root, Func<LspConnection, string, CancellationToken, Task<int>> body, CancellationToken ct) {
        await using var connection = await LspConnection.StartAsync(entry, root, ct).ConfigureAwait(false);
        return await body(connection, entry.LanguageId, ct).ConfigureAwait(false);
    }

    private static async Task<int> LspLocationToolAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct, string method, string label) {
        var file = ResolveFile(Required(o, "file_path", method));
        var line = GetInt(o, "line", 1);
        var character = GetInt(o, "character", 1);
        return await RunWithServerAsync(o, file, async (conn, languageId, token) => {
            await conn.OpenFileAsync(file, languageId, token).ConfigureAwait(false);
            var result = await conn.RequestAsync(method, PositionParams(file, line, character, method == "textDocument/references"), token).ConfigureAwait(false);
            var locations = ParseLocations(result);

            var sb = new StringBuilder();
            if (locations.Count == 0) {
                sb.AppendLine($"未找到{label}（{Path.GetFileName(file)}:{line}:{character}）");
                Console.Out.Write(sb.ToString());
                return 0;
            }

            if (method == "textDocument/references") {
                sb.AppendLine($"找到 {locations.Count} 处{label}");
                sb.AppendLine();
                foreach (var group in locations.GroupBy(l => l.Uri, StringComparer.Ordinal)) {
                    sb.AppendLine($"文件 {UriToPath(group.Key)}");
                    foreach (var loc in group.OrderBy(l => l.Line)) {
                        sb.AppendLine($"   行 {(loc.Line + 1).ToString(CultureInfo.InvariantCulture)}, 列 {(loc.Character + 1).ToString(CultureInfo.InvariantCulture)}");
                    }
                    sb.AppendLine();
                }
            } else {
                sb.AppendLine($"找到 {locations.Count} 处{label}");
                sb.AppendLine();
                for (var i = 0; i < locations.Count; i++) {
                    sb.AppendLine($"{i + 1}. {UriToPath(locations[i].Uri)}");
                    sb.AppendLine($"   行 {(locations[i].Line + 1).ToString(CultureInfo.InvariantCulture)}, 列 {(locations[i].Character + 1).ToString(CultureInfo.InvariantCulture)}");
                }
            }
            Console.Out.Write(sb.ToString());
            return 0;
        }, ct).ConfigureAwait(false);
    }

    private static async Task<int> LspPositionToolAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct, string method, Action<StringBuilder, JsonNode?> print) {
        var file = ResolveFile(Required(o, "file_path", method));
        var line = GetInt(o, "line", 1);
        var character = GetInt(o, "character", 1);
        return await RunWithServerAsync(o, file, async (conn, languageId, token) => {
            await conn.OpenFileAsync(file, languageId, token).ConfigureAwait(false);
            var result = await conn.RequestAsync(method, PositionParams(file, line, character, false), token).ConfigureAwait(false);
            var sb = new StringBuilder();
            print(sb, result);
            Console.Out.Write(sb.ToString());
            return 0;
        }, ct).ConfigureAwait(false);
    }

    private static async Task<int> LspCallHierarchyToolAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct, string method, string label) {
        var file = ResolveFile(Required(o, "file_path", method));
        var line = GetInt(o, "line", 1);
        var character = GetInt(o, "character", 1);
        return await RunWithServerAsync(o, file, async (conn, languageId, token) => {
            await conn.OpenFileAsync(file, languageId, token).ConfigureAwait(false);
            var prepared = await conn.RequestAsync("textDocument/prepareCallHierarchy", PositionParams(file, line, character, false), token).ConfigureAwait(false);
            if (prepared is not JsonArray { Count: > 0 } items || items[0] is not JsonObject item) {
                Console.Out.WriteLine($"未能解析调用层级（{Path.GetFileName(file)}:{line}:{character}），服务器可能还未索引该位置");
                return 0;
            }

            var result = await conn.RequestAsync(method, new JsonObject { ["item"] = item.DeepClone() }, token).ConfigureAwait(false);
            var sb = new StringBuilder();
            sb.AppendLine($"{label}（{LspJson.Str(item["name"])}）");
            sb.AppendLine();
            if (result is JsonArray { Count: > 0 } calls) {
                foreach (var call in calls) {
                    var target = call?["from"] ?? call?["to"];
                    var targetLine = LspJson.Int(target?["range"]?["start"]?["line"]) + 1;
                    sb.AppendLine($"  {LspJson.Str(target?["name"])} [{KindName(LspJson.Int(target?["kind"]))}] {UriToPath(LspJson.Str(target?["uri"]))}:{targetLine.ToString(CultureInfo.InvariantCulture)}");
                }
                sb.AppendLine();
                sb.AppendLine($"共 {calls.Count} 项");
            } else {
                sb.AppendLine("无结果");
            }
            Console.Out.Write(sb.ToString());
            return 0;
        }, ct).ConfigureAwait(false);
    }

    // ==================== 结果渲染 ====================

    private static void PrintHover(StringBuilder sb, JsonNode? result) {
        var text = HoverText(result?["contents"]);
        sb.AppendLine(string.IsNullOrWhiteSpace(text) ? "无悬停信息" : text);
    }

    private static string HoverText(JsonNode? contents) => contents switch {
        null => "",
        JsonValue value => LspJson.Str(value),
        JsonArray array => string.Join("\n", array.Select(HoverText).Where(text => text.Length > 0)),
        JsonObject obj => LspJson.Str(obj["value"]) is { Length: > 0 } value ? value : LspJson.Str(obj["text"]),
        _ => contents.ToJsonString()
    };

    private static void PrintCompletion(StringBuilder sb, JsonNode? result) {
        var items = result is JsonObject list ? list["items"] as JsonArray : result as JsonArray;
        if (items is not { Count: > 0 }) {
            sb.AppendLine("无补全建议（服务器可能还未完成索引）");
            return;
        }
        sb.AppendLine($"补全建议 {items.Count} 条");
        sb.AppendLine();
        foreach (var item in items.Take(50)) {
            var detail = LspJson.Str(item?["detail"]);
            var label = LspJson.Str(item?["label"]);
            sb.AppendLine(detail.Length > 0 ? $"  {label}  —  {detail}" : $"  {label}");
        }
    }

    private static void PrintDocumentSymbols(StringBuilder sb, JsonArray symbols, int depth) {
        var indent = new string(' ', depth * 2);
        foreach (var node in symbols) {
            var line = LspJson.Int((node?["selectionRange"] ?? node?["range"])?["start"]?["line"]) + 1;
            sb.AppendLine($"{indent}{LspJson.Str(node?["name"])} [{KindName(LspJson.Int(node?["kind"]))}] 行 {line.ToString(CultureInfo.InvariantCulture)}");
            if (node?["children"] is JsonArray { Count: > 0 } children) PrintDocumentSymbols(sb, children, depth + 1);
        }
    }

    private static string KindName(int kind) => kind switch {
        1 => "File", 2 => "Module", 3 => "Namespace", 4 => "Package", 5 => "Class", 6 => "Method",
        7 => "Property", 8 => "Field", 9 => "Constructor", 10 => "Enum", 11 => "Interface", 12 => "Function",
        13 => "Variable", 14 => "Constant", 15 => "String", 16 => "Number", 17 => "Boolean", 18 => "Array",
        19 => "Object", 20 => "Key", 21 => "Null", 22 => "EnumMember", 23 => "Struct", 24 => "Event",
        25 => "Operator", 26 => "TypeParameter", _ => "Unknown"
    };

    private sealed record LspLocation(string Uri, int Line, int Character);

    private static List<LspLocation> ParseLocations(JsonNode? result) {
        var locations = new List<LspLocation>();
        if (result is not JsonArray array) return locations;
        foreach (var node in array) {
            // Location { uri, range } 与 LocationLink { targetUri, targetRange } 两种形态都收
            var uri = LspJson.Str(node?["uri"]);
            var range = node?["range"];
            if (uri.Length == 0) {
                uri = LspJson.Str(node?["targetUri"]);
                range = node?["targetRange"] ?? node?["targetSelectionRange"];
            }
            if (uri.Length == 0) continue;
            locations.Add(new LspLocation(uri, LspJson.Int(range?["start"]?["line"]), LspJson.Int(range?["start"]?["character"])));
        }
        return locations;
    }

    private static JsonObject PositionParams(string file, int line, int character, bool withReferencesContext) {
        var parameters = new JsonObject {
            ["textDocument"] = new JsonObject { ["uri"] = ToFileUri(file) },
            // 工具参数 1-based（与编辑器一致），LSP 0-based
            ["position"] = new JsonObject { ["line"] = Math.Max(0, line - 1), ["character"] = Math.Max(0, character - 1) }
        };
        if (withReferencesContext) parameters["context"] = new JsonObject { ["includeDeclaration"] = true };
        return parameters;
    }

    private static string ResolveFile(string path) {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) throw new ArgumentException($"文件不存在: {full}");
        if (new FileInfo(full).Length > MaxLspFileSizeBytes)
            throw new ArgumentException($"文件超过 {MaxLspFileSizeBytes / 1_000_000}MB，LSP 分析跳过: {full}");
        return full;
    }

    private static string? ResolveWorkspaceHint(IReadOnlyDictionary<string, string> o) => Nullable(o, "workspace_path");

    /// <summary>向上找 .git 得工作区根；找不到就用起始目录 —— 同时作为 rootUri 与服务器进程 CWD。</summary>
    private static string ResolveWorkspace(string start) {
        var full = Path.GetFullPath(start);
        var begin = Directory.Exists(full) ? new DirectoryInfo(full) : new DirectoryInfo(Path.GetDirectoryName(full) ?? full);
        for (var current = begin; current is not null; current = current.Parent) {
            var git = Path.Combine(current.FullName, ".git");
            if (Directory.Exists(git) || File.Exists(git)) return current.FullName;
        }
        return Directory.Exists(full) ? full : Directory.GetCurrentDirectory();
    }

    private static string ToFileUri(string path) => new Uri(path).AbsoluteUri;

    private static string UriToPath(string uri) {
        if (uri.Length == 0) return "";
        return Uri.TryCreate(uri, UriKind.Absolute, out var parsed) && parsed.IsFile ? parsed.LocalPath : uri;
    }
}
