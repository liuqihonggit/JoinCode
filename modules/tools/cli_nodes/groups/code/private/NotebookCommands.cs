using System.Collections.Frozen;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using JoinCode.Abstractions.Tools;

namespace JoinCode.CliNode.Code;

/// <summary>
/// code 分组的本地命令表 —— Jupyter notebook 的 .ipynb 是纯 JSON，用 BCL 直接读写即可，
/// 不需要宿主注入 INotebookService。
/// <para>本地实现 10 个 notebook_* 工具；lsp_*（需语言服务器进程）、generate_*/analyze_*（需模型）、
/// recall_*（需宿主召回库）继续转发宿主。</para>
/// <para>nbformat 约定按 4.x：cells[].cell_type / source / metadata，代码单元另有 outputs / execution_count。</para>
/// </summary>
internal static class NotebookCommands {
    private static readonly FrozenDictionary<string, CliNodeHandler> Handlers =
        new Dictionary<string, CliNodeHandler>(StringComparer.Ordinal) {
            ["notebook_create"] = CreateAsync,
            ["notebook_read"] = ReadAsync,
            ["notebook_add_cell"] = AddCellAsync,
            ["notebook_delete_cell"] = DeleteCellAsync,
            ["notebook_edit_cell"] = EditCellAsync,
            ["notebook_move_cell"] = MoveCellAsync,
            ["notebook_change_cell_type"] = ChangeCellTypeAsync,
            ["notebook_clear_outputs"] = ClearOutputsAsync,
            ["notebook_get_cell"] = GetCellAsync,
            ["notebook_edit"] = EditAsync
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static CliNodeHandler? Find(string toolId) => Handlers.TryGetValue(toolId, out var handler) ? handler : null;

    public static IEnumerable<string> LocalToolIds => Handlers.Keys.OrderBy(id => id, StringComparer.Ordinal);

    private const int PreviewLength = 80;

    private static Task<int> CreateAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var path = EnsureExtension(Required(o, "file_path", "notebook_create"));
        if (File.Exists(path))
            throw new ArgumentException($"notebook_create 目标已存在: {path}。改用 notebook_read 查看，或换一个路径。");
        var kernel = Nullable(o, "kernel_name") ?? "python3";
        var language = Nullable(o, "language") ?? "python";

        var notebook = new JsonObject {
            ["cells"] = new JsonArray(),
            ["metadata"] = new JsonObject {
                ["kernelspec"] = new JsonObject {
                    ["display_name"] = kernel,
                    ["language"] = language,
                    ["name"] = kernel
                },
                ["language_info"] = new JsonObject { ["name"] = language }
            },
            ["nbformat"] = 4,
            ["nbformat_minor"] = 5
        };
        Save(path, notebook);

        Console.Out.WriteLine($"已创建 notebook: {path}");
        Console.Out.WriteLine($"格式版本: 4.5");
        Console.Out.WriteLine($"内核: {kernel} ({language})");
        return Task.FromResult(0);
    }

    private static Task<int> ReadAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var path = Required(o, "file_path", "notebook_read");
        var showContent = GetBool(o, "show_content", false);
        var notebook = Load(path);
        var cells = Cells(notebook);

        var code = 0;
        var markdown = 0;
        foreach (var cell in cells) {
            var type = CellType(cell);
            if (type == "code") code++;
            else if (type == "markdown") markdown++;
        }

        Console.Out.WriteLine("Notebook 信息:");
        Console.Out.WriteLine($"路径: {path}");
        Console.Out.WriteLine($"格式版本: {Int(notebook, "nbformat")}.{Int(notebook, "nbformat_minor")}");
        Console.Out.WriteLine($"单元总数: {cells.Count}");
        Console.Out.WriteLine($"代码单元: {code}");
        Console.Out.WriteLine($"Markdown 单元: {markdown}");
        if (notebook["metadata"] is JsonObject metadata && metadata["kernelspec"] is JsonObject kernel)
            Console.Out.WriteLine($"内核: {Str(kernel, "display_name")} ({Str(kernel, "language")})");

        Console.Out.WriteLine();
        Console.Out.WriteLine("单元列表:");
        for (var i = 0; i < cells.Count; i++)
            Console.Out.WriteLine($"  [{i}] {CellType(cells[i]),-10} {Preview(CellSource(cells[i]))}");

        if (showContent && cells.Count > 0) {
            Console.Out.WriteLine();
            Console.Out.WriteLine("单元内容:");
            for (var i = 0; i < cells.Count; i++) {
                Console.Out.WriteLine();
                Console.Out.WriteLine($"=== cell [{i}] ({CellType(cells[i])}) ===");
                Console.Out.WriteLine(CellSource(cells[i]));
            }
        }
        return Task.FromResult(0);
    }

    private static Task<int> AddCellAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var path = Required(o, "file_path", "notebook_add_cell");
        var type = NormalizeCellType(Required(o, "cell_type", "notebook_add_cell"));
        var content = Required(o, "content", "notebook_add_cell");
        var notebook = Load(path);
        var cells = Cells(notebook);

        var index = GetInt(o, "index", cells.Count);
        if (index < 0 || index > cells.Count)
            throw new ArgumentException($"index 超出范围: {index}（当前 {cells.Count} 个单元，可插入位置 0..{cells.Count}）。");

        cells.Insert(index, NewCell(type, content, NextCellId(notebook)));
        Save(path, notebook);
        Console.Out.WriteLine($"已在位置 [{index}] 插入 {type} 单元，当前共 {cells.Count} 个单元。");
        return Task.FromResult(0);
    }

    private static Task<int> DeleteCellAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var path = Required(o, "file_path", "notebook_delete_cell");
        var index = GetInt(o, "index", -1);
        var notebook = Load(path);
        var cells = Cells(notebook);
        EnsureIndex(index, cells.Count, "notebook_delete_cell");
        cells.RemoveAt(index);
        Save(path, notebook);
        Console.Out.WriteLine($"已删除单元 [{index}]，当前共 {cells.Count} 个单元。");
        return Task.FromResult(0);
    }

    private static Task<int> EditCellAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var path = Required(o, "file_path", "notebook_edit_cell");
        var index = GetInt(o, "index", -1);
        var content = Required(o, "content", "notebook_edit_cell");
        var notebook = Load(path);
        var cells = Cells(notebook);
        EnsureIndex(index, cells.Count, "notebook_edit_cell");
        var cell = CellAt(cells, index);
        SetSource(cell, content);
        // 源码变了，旧的执行产物已失效，留着会让 LLM 误以为还是新代码的输出。
        if (CellType(cell) == "code") ResetOutputs(cell);
        Save(path, notebook);
        Console.Out.WriteLine($"已更新单元 [{index}] 的内容（{content.Length} 字符）。");
        return Task.FromResult(0);
    }

    private static Task<int> MoveCellAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var path = Required(o, "file_path", "notebook_move_cell");
        var from = GetInt(o, "from_index", -1);
        var to = GetInt(o, "to_index", -1);
        var notebook = Load(path);
        var cells = Cells(notebook);
        EnsureIndex(from, cells.Count, "notebook_move_cell");
        if (to < 0 || to >= cells.Count)
            throw new ArgumentException($"to_index 超出范围: {to}（当前 {cells.Count} 个单元，合法 0..{cells.Count - 1}）。");

        var cell = cells[from]!;
        cells.RemoveAt(from);
        cells.Insert(to, cell);
        Save(path, notebook);
        Console.Out.WriteLine($"已把单元 [{from}] 移动到 [{to}]。");
        return Task.FromResult(0);
    }

    private static Task<int> ChangeCellTypeAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var path = Required(o, "file_path", "notebook_change_cell_type");
        var index = GetInt(o, "index", -1);
        var newType = NormalizeCellType(Required(o, "new_type", "notebook_change_cell_type"));
        var notebook = Load(path);
        var cells = Cells(notebook);
        EnsureIndex(index, cells.Count, "notebook_change_cell_type");
        var cell = CellAt(cells, index);
        var oldType = CellType(cell);
        cell["cell_type"] = newType;
        if (newType == "code") {
            if (cell["outputs"] is null) cell["outputs"] = new JsonArray();
            if (cell["execution_count"] is null) cell["execution_count"] = null;
        } else {
            cell.Remove("outputs");
            cell.Remove("execution_count");
        }
        Save(path, notebook);
        Console.Out.WriteLine($"已把单元 [{index}] 从 {oldType} 改为 {newType}。");
        return Task.FromResult(0);
    }

    private static Task<int> ClearOutputsAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var path = Required(o, "file_path", "notebook_clear_outputs");
        var notebook = Load(path);
        var cells = Cells(notebook);
        var cleared = 0;
        foreach (var node in cells) {
            if (node is not JsonObject cell || CellType(cell) != "code") continue;
            if (HasOutputs(cell)) cleared++;
            ResetOutputs(cell);
        }
        Save(path, notebook);
        Console.Out.WriteLine($"已清空 {cleared} 个代码单元的输出（共 {cells.Count} 个单元）。");
        return Task.FromResult(0);
    }

    private static Task<int> GetCellAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var path = Required(o, "file_path", "notebook_get_cell");
        var index = GetInt(o, "index", -1);
        var notebook = Load(path);
        var cells = Cells(notebook);
        EnsureIndex(index, cells.Count, "notebook_get_cell");
        var cell = CellAt(cells, index);
        Console.Out.WriteLine($"[{index}] {CellType(cell)}");
        Console.Out.WriteLine("---");
        Console.Out.WriteLine(CellSource(cell));
        return Task.FromResult(0);
    }

    /// <summary>按 cell id 做 replace / insert / delete —— 对齐宿主 notebook_edit 的三种编辑模式。</summary>
    private static Task<int> EditAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var path = Required(o, "notebook_path", "notebook_edit");
        var source = Required(o, "new_source", "notebook_edit");
        var mode = (Nullable(o, "edit_mode") ?? "replace").Trim().ToLowerInvariant();
        var cellId = Nullable(o, "cell_id");
        var notebook = Load(path);
        var cells = Cells(notebook);

        switch (mode) {
            case "insert": {
                var type = NormalizeCellType(Nullable(o, "cell_type")
                    ?? throw new ArgumentException("insert 模式必须提供 --cell_type（code 或 markdown）。"));
                var position = cellId is null ? cells.Count : IndexOfCell(cells, cellId) + 1;
                cells.Insert(Math.Clamp(position, 0, cells.Count), NewCell(type, source, cellId ?? NextCellId(notebook)));
                Save(path, notebook);
                Console.Out.WriteLine($"已在位置 [{position}] 插入 {type} 单元（cell_id={cellId ?? "(自动生成)"}）。");
                return Task.FromResult(0);
            }
            case "delete": {
                var index = RequireCellIndex(cells, cellId, "delete");
                cells.RemoveAt(index);
                Save(path, notebook);
                Console.Out.WriteLine($"已删除 cell_id={cellId}（原位置 [{index}]）。");
                return Task.FromResult(0);
            }
            case "replace": {
                var index = RequireCellIndex(cells, cellId, "replace");
                var cell = CellAt(cells, index);
                SetSource(cell, source);
                if (CellType(cell) == "code") ResetOutputs(cell);
                var requestedType = Nullable(o, "cell_type");
                if (requestedType is not null) cell["cell_type"] = NormalizeCellType(requestedType);
                Save(path, notebook);
                Console.Out.WriteLine($"已替换 cell_id={cellId}（位置 [{index}]）的内容（{source.Length} 字符）。");
                return Task.FromResult(0);
            }
            default:
                throw new ArgumentException($"edit_mode 只能是 replace / insert / delete，当前 '{mode}'。");
        }
    }

    // ── notebook 读写 ────────────────────────────────────────────────────────

    private static JsonObject Load(string path) {
        if (!File.Exists(path))
            throw new ArgumentException($"notebook 不存在: {path}。先用 notebook_create 创建，或检查路径。");
        string text;
        try {
            text = File.ReadAllText(path);
        } catch (IOException ex) {
            throw new IOException($"读取 notebook 失败: {path}（{ex.Message}）");
        }
        JsonNode? node;
        try {
            node = JsonNode.Parse(text, documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip });
        } catch (JsonException ex) {
            throw new ArgumentException($"notebook 不是合法 JSON: {path}（{ex.Message}）");
        }
        if (node is not JsonObject notebook || notebook["cells"] is not JsonArray)
            throw new ArgumentException($"notebook 结构不合 nbformat: {path} 缺少 cells 数组。");
        return notebook;
    }

    private static void Save(string path, JsonObject notebook) {
        var directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        var json = notebook.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(path, json + "\n", new UTF8Encoding(false));
    }

    private static JsonArray Cells(JsonObject notebook) => (JsonArray)notebook["cells"]!;

    private static JsonObject NewCell(string type, string content, string id) {
        var cell = new JsonObject {
            ["cell_type"] = type,
            ["id"] = id,
            ["metadata"] = new JsonObject(),
            ["source"] = SourceLines(content)
        };
        if (type == "code") {
            cell["execution_count"] = null;
            cell["outputs"] = new JsonArray();
        }
        return cell;
    }

    /// <summary>nbformat 的 source 既允许字符串也允许行数组；统一写成行数组（Jupyter 自身写法）。</summary>
    private static JsonArray SourceLines(string content) {
        var parts = content.Split('\n');
        var values = new List<JsonNode>(parts.Length);
        for (var i = 0; i < parts.Length; i++) {
            var isLast = i == parts.Length - 1;
            if (isLast && parts[i].Length == 0) break;
            // 用 JsonValue.Create + 数组构造器：JsonArray.Add<T> 在 AOT 下会触发 IL3050/IL2026。
            values.Add(JsonValue.Create(isLast ? parts[i] : parts[i] + "\n")!);
        }
        return new JsonArray(values.ToArray());
    }

    private static string CellSource(JsonNode? cell) {
        if (cell is not JsonObject body) return string.Empty;
        var source = body["source"];
        if (source is null) return string.Empty;
        if (source is JsonArray array) {
            var builder = new StringBuilder();
            foreach (var line in array) builder.Append(line?.GetValue<string>() ?? string.Empty);
            return builder.ToString();
        }
        return source.GetValue<string>();
    }

    private static void SetSource(JsonObject cell, string content) => cell["source"] = SourceLines(content);

    private static string CellType(JsonNode? cell) => cell is JsonObject body ? Str(body, "cell_type") : string.Empty;

    private static bool HasOutputs(JsonNode? cell) =>
        cell is JsonObject body && body["outputs"] is JsonArray outputs && outputs.Count > 0;

    private static void ResetOutputs(JsonNode? cell) {
        if (cell is not JsonObject body) return;
        body["outputs"] = new JsonArray();
        body["execution_count"] = null;
    }

    /// <summary>按下标取可变单元对象；结构损坏（cells 里混了非对象）时明确报错而不是空引用。</summary>
    private static JsonObject CellAt(JsonArray cells, int index) =>
        cells[index] as JsonObject
        ?? throw new ArgumentException($"单元 [{index}] 不是 JSON 对象，notebook 结构损坏。");

    private static string NextCellId(JsonObject notebook) {
        var cells = Cells(notebook);
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var cell in cells) {
            var id = cell?["id"]?.GetValue<string>();
            if (!string.IsNullOrEmpty(id)) used.Add(id);
        }
        string candidate;
        do {
            candidate = Guid.NewGuid().ToString("N")[..8];
        } while (used.Contains(candidate));
        return candidate;
    }

    private static int IndexOfCell(JsonArray cells, string cellId) {
        for (var i = 0; i < cells.Count; i++) {
            if (cells[i]?["id"]?.GetValue<string>() == cellId) return i;
        }
        // 兼容 4.0~4.4 的无 id notebook：允许直接传下标字符串。
        if (int.TryParse(cellId, out var index) && index >= 0 && index < cells.Count) return index;
        throw new ArgumentException($"找不到 cell_id='{cellId}'。用 notebook_read 查看现有单元与 id。");
    }

    private static int RequireCellIndex(JsonArray cells, string? cellId, string mode) {
        if (string.IsNullOrWhiteSpace(cellId))
            throw new ArgumentException($"{mode} 模式必须提供 --cell_id。用 notebook_read 查看单元 id。");
        return IndexOfCell(cells, cellId);
    }

    private static void EnsureIndex(int index, int count, string toolId) {
        if (index < 0 || index >= count)
            throw new ArgumentException($"{toolId} 的 index 超出范围: {index}（当前 {count} 个单元，合法 0..{Math.Max(0, count - 1)}）。");
    }

    private static string NormalizeCellType(string raw) => raw.Trim().ToLowerInvariant() switch {
        "code" => "code",
        "markdown" or "md" => "markdown",
        "raw" => "raw",
        _ => throw new ArgumentException($"cell_type 只能是 code / markdown / raw，当前 '{raw}'。")
    };

    private static string EnsureExtension(string path) =>
        path.EndsWith(".ipynb", StringComparison.OrdinalIgnoreCase) ? path : path + ".ipynb";

    private static string Preview(string content) {
        foreach (var line in content.Split('\n')) {
            var trimmed = line.Trim();
            if (trimmed.Length == 0) continue;
            return trimmed.Length <= PreviewLength ? trimmed : trimmed[..PreviewLength] + "…";
        }
        return "(空)";
    }

    private static int Int(JsonObject node, string key) =>
        node[key] is JsonValue value && value.TryGetValue<int>(out var number) ? number : 0;

    private static string Str(JsonObject node, string key) =>
        node[key] is JsonValue value && value.TryGetValue<string>(out var text) ? text : string.Empty;

    private static string Required(IReadOnlyDictionary<string, string> o, string key, string toolId) {
        if (o.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)) return value;
        throw new ArgumentException($"工具 '{toolId}' 缺少必填参数 --{key}。正确写法: {toolId} -h 查看参数。");
    }

    private static string? Nullable(IReadOnlyDictionary<string, string> o, string key) =>
        o.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static int GetInt(IReadOnlyDictionary<string, string> o, string key, int fallback) {
        if (!o.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) return fallback;
        if (int.TryParse(raw, out var value)) return value;
        throw new ArgumentException($"参数 --{key} 需要整数，当前值 '{raw}'。");
    }

    private static bool GetBool(IReadOnlyDictionary<string, string> o, string key, bool fallback) {
        if (!o.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) return fallback;
        return raw.Trim().ToLowerInvariant() switch {
            "true" or "1" or "yes" or "y" or "on" => true,
            "false" or "0" or "no" or "n" or "off" => false,
            _ => throw new ArgumentException($"参数 --{key} 需要布尔值，当前值 '{raw}'。")
        };
    }
}
