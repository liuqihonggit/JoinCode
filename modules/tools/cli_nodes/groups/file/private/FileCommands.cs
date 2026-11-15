using System.Collections.Frozen;
using System.Text.RegularExpressions;
using JoinCode.Abstractions.Tools;

namespace JoinCode.CliNode.File;

/// <summary>
/// file 分组的本地命令表 — 只用 BCL，不引用引擎 / DI / IFileSystem。
/// file_apply_patch 的实现在 FilePatchCommands（同目录）；未登记的 send_user_file 需要宿主 UI 通道，转发宿主。
/// 参数名与 [McpToolParameter] 特性逐一对应，说明不在此重复。
/// </summary>
internal static class FileCommands {
    private static readonly FrozenDictionary<string, CliNodeHandler> Handlers =
        new Dictionary<string, CliNodeHandler>(StringComparer.Ordinal) {
            ["file_read"] = ReadAsync,
            ["file_write"] = WriteAsync,
            ["file_delete"] = DeleteAsync,
            ["file_edit"] = EditAsync,
            ["file_edit_regex"] = EditRegexAsync,
            ["file_insert_lines"] = InsertLinesAsync,
            ["file_delete_lines"] = DeleteLinesAsync,
            ["file_batch_edit"] = BatchEditAsync,
            ["directory_list"] = DirectoryListAsync,
            ["file_snip_lines"] = SnipLinesAsync,
            ["file_snip_preview"] = SnipPreviewAsync,
            ["file_apply_patch"] = FilePatchCommands.ApplyPatchAsync
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static CliNodeHandler? Find(string toolId) => Handlers.TryGetValue(toolId, out var handler) ? handler : null;

    public static IEnumerable<string> LocalToolIds => Handlers.Keys.OrderBy(id => id, StringComparer.Ordinal);

    private static Task<int> ReadAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        var path = Required(o, "file_path", "file_read");
        var lines = ReadLines(path);
        var offset = Math.Max(1, GetInt(o, "offset", 1));
        var limit = GetInt(o, "limit", lines.Length);
        foreach (var line in lines.Skip(offset - 1).Take(limit))
            Console.Out.WriteLine(line);
        return Task.FromResult(0);
    }

    private static Task<int> WriteAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        var path = Required(o, "file_path", "file_write");
        var content = Required(o, "content", "file_write");
        global::System.IO.File.WriteAllText(path, content);
        Console.Out.WriteLine($"已写入 {path} ({content.Length} 字符)");
        return Task.FromResult(0);
    }

    private static Task<int> DeleteAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        var path = Required(o, "file_path", "file_delete");
        if (!global::System.IO.File.Exists(path))
            throw new ArgumentException($"file_delete 目标文件不存在: {path}。正确写法: 先用 directory_list 确认路径。");
        global::System.IO.File.Delete(path);
        Console.Out.WriteLine($"已删除 {path}");
        return Task.FromResult(0);
    }

    private static Task<int> EditAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        var path = Required(o, "file_path", "file_edit");
        var oldValue = Required(o, "old_string", "file_edit");
        var newValue = Required(o, "new_string", "file_edit");
        var text = global::System.IO.File.ReadAllText(path);
        var replaced = IsTrue(o, "replace_all")
            ? text.Replace(oldValue, newValue, StringComparison.Ordinal)
            : ReplaceFirst(text, oldValue, newValue);
        return WriteChangedAsync(path, text, replaced, "file_edit");
    }

    private static Task<int> EditRegexAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        var path = Required(o, "file_path", "file_edit_regex");
        var pattern = Required(o, "pattern", "file_edit_regex");
        var replacement = Required(o, "replacement", "file_edit_regex");
        var text = global::System.IO.File.ReadAllText(path);
        var regex = new Regex(pattern);
        var replaced = IsTrue(o, "replace_all", fallback: true)
            ? regex.Replace(text, replacement)
            : regex.Replace(text, replacement, 1);
        return WriteChangedAsync(path, text, replaced, "file_edit_regex");
    }

    private static Task<int> InsertLinesAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        var path = Required(o, "file_path", "file_insert_lines");
        var afterLine = GetInt(o, "after_line", 0);
        var newContent = Required(o, "new_content", "file_insert_lines");
        var lines = ReadLines(path).ToList();
        var index = Math.Clamp(afterLine, 0, lines.Count);
        lines.InsertRange(index, newContent.Split('\n'));
        global::System.IO.File.WriteAllLines(path, lines);
        Console.Out.WriteLine($"已在 {path} 第 {afterLine} 行后插入内容");
        return Task.FromResult(0);
    }

    private static Task<int> DeleteLinesAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        var path = Required(o, "file_path", "file_delete_lines");
        var start = Math.Max(1, GetInt(o, "start_line", 1));
        var end = GetInt(o, "end_line", start);
        if (end < start)
            throw new ArgumentException($"file_delete_lines 的 --end_line ({end}) 不能小于 --start_line ({start})。正确写法: 检查行号范围。");
        var lines = ReadLines(path).ToList();
        var count = Math.Min(end, lines.Count) - start + 1;
        if (count > 0) lines.RemoveRange(start - 1, count);
        global::System.IO.File.WriteAllLines(path, lines);
        Console.Out.WriteLine($"已删除 {path} 第 {start}-{end} 行");
        return Task.FromResult(0);
    }

    private static Task<int> BatchEditAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var oldValue = Required(o, "old_string", "file_batch_edit");
        var newValue = Required(o, "new_string", "file_batch_edit");
        var pathsRaw = Required(o, "file_paths", "file_batch_edit");
        var paths = pathsRaw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        var changed = 0;
        foreach (var path in paths) {
            ct.ThrowIfCancellationRequested();
            if (!global::System.IO.File.Exists(path)) continue;
            var text = global::System.IO.File.ReadAllText(path);
            var replaced = IsTrue(o, "replace_all", fallback: true)
                ? text.Replace(oldValue, newValue, StringComparison.Ordinal)
                : ReplaceFirst(text, oldValue, newValue);
            if (string.Equals(text, replaced, StringComparison.Ordinal)) continue;
            global::System.IO.File.WriteAllText(path, replaced);
            changed++;
        }
        Console.Out.WriteLine($"批量编辑完成，修改 {changed}/{paths.Length} 个文件");
        return Task.FromResult(0);
    }

    private static Task<int> DirectoryListAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        var path = Required(o, "directory_path", "directory_list");
        if (!Directory.Exists(path))
            throw new ArgumentException($"directory_list 目录不存在: {path}。正确写法: 传入存在的目录路径。");
        var recursive = IsTrue(o, "recursive");
        var option = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        foreach (var entry in Directory.EnumerateFileSystemEntries(path, "*", option).OrderBy(e => e, StringComparer.Ordinal))
            Console.Out.WriteLine(entry);
        return Task.FromResult(0);
    }

    private static Task<int> SnipLinesAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        var path = Required(o, "file_path", "file_snip_lines");
        var start = Math.Max(0, GetInt(o, "start_line", 0));
        var count = GetInt(o, "line_count", 100);
        foreach (var line in ReadLines(path).Skip(start).Take(count))
            Console.Out.WriteLine(line);
        return Task.FromResult(0);
    }

    private static Task<int> SnipPreviewAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        var path = Required(o, "file_path", "file_snip_preview");
        var maxLines = GetInt(o, "max_preview_lines", 20);
        var lines = ReadLines(path);
        Console.Out.WriteLine($"文件: {path}");
        Console.Out.WriteLine($"大小: {new FileInfo(path).Length} 字节");
        Console.Out.WriteLine($"行数: {lines.Length}");
        foreach (var line in lines.Take(maxLines))
            Console.Out.WriteLine(line);
        return Task.FromResult(0);
    }

    private static Task<int> WriteChangedAsync(string path, string before, string after, string toolId) {
        if (string.Equals(before, after, StringComparison.Ordinal)) {
            Console.Out.WriteLine($"{toolId}: 未找到可替换内容，文件未改动 ({path})");
            return Task.FromResult(0);
        }
        global::System.IO.File.WriteAllText(path, after);
        Console.Out.WriteLine($"{toolId}: 已更新 {path}");
        return Task.FromResult(0);
    }

    private static string ReplaceFirst(string text, string oldValue, string newValue) {
        var index = text.IndexOf(oldValue, StringComparison.Ordinal);
        return index < 0 ? text : text[..index] + newValue + text[(index + oldValue.Length)..];
    }

    private static string[] ReadLines(string path) {
        if (!global::System.IO.File.Exists(path))
            throw new ArgumentException($"文件不存在: {path}。正确写法: 先用 directory_list 确认路径。");
        return global::System.IO.File.ReadAllLines(path);
    }

    private static string Required(IReadOnlyDictionary<string, string> o, string key, string toolId) =>
        o.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw new ArgumentException($"{toolId} 缺少必填参数 --{key}（由 [McpToolParameter] 标记为必填）。正确写法: jcc-tool-file.exe {toolId} --{key} <值>");

    private static string Get(IReadOnlyDictionary<string, string> o, string key, string fallback) =>
        o.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static bool IsTrue(IReadOnlyDictionary<string, string> o, string key, bool fallback = false) =>
        o.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) ? parsed : fallback;

    private static int GetInt(IReadOnlyDictionary<string, string> o, string key, int fallback) =>
        int.TryParse(Get(o, key, string.Empty), out var parsed) ? parsed : fallback;
}
