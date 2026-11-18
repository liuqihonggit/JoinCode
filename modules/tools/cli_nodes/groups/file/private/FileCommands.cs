





namespace JoinCode.CliNode.File;

/// <summary>
/// file 分组的本地命令表 — 只用 BCL，不引用引擎 / DI / IFileSystem。
/// file_apply_patch 的实现在 FilePatchCommands（同目录）；send_user_file 是纯文件读取+摘要，本地实现。
/// 参数名与 [McpToolParameter] 特性逐一对应，说明不在此重复。
/// </summary>
internal static partial class FileCommands {
    private static readonly FrozenDictionary<string, CliNodeHandler> Handlers =
        new Dictionary<string, CliNodeHandler>(StringComparer.Ordinal) {
            ["read"] = ReadAsync,
            ["write"] = WriteAsync,
            ["file_delete"] = DeleteAsync,
            ["edit"] = EditAsync,
            ["file_edit_regex"] = EditRegexAsync,
            ["file_insert_lines"] = InsertLinesAsync,
            ["file_delete_lines"] = DeleteLinesAsync,
            ["file_batch_edit"] = BatchEditAsync,
            ["directory_list"] = DirectoryListAsync,
            ["file_snip_lines"] = SnipLinesAsync,
            ["file_snip_preview"] = SnipPreviewAsync,
            ["apply_patch"] = FilePatchCommands.ApplyPatchAsync,
            ["send_user_file"] = SendUserFileAsync
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static CliNodeHandler? Find(string toolId) => Handlers.TryGetValue(toolId, out var handler) ? handler : null;

    public static IEnumerable<string> LocalToolIds => Handlers.Keys.OrderBy(id => id, StringComparer.Ordinal);

    private static Task<int> ReadAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        var path = Required(o, "file_path", "read");
        var lines = ReadLines(path);
        var offset = Math.Max(1, GetInt(o, "offset", 1));
        var limit = GetInt(o, "limit", lines.Length);
        foreach (var line in lines.Skip(offset - 1).Take(limit))
            Console.Out.WriteLine(line);
        return Task.FromResult(0);
    }

    private static Task<int> WriteAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        var path = Required(o, "file_path", "write");
        var content = Required(o, "content", "write");
        // 覆盖已有文件时沿用它的编码与签名；新建文件就是无 BOM UTF-8
        WriteText(path, content, global::System.IO.File.Exists(path) ? DetectEncoding(path) : BclFileIO.Utf8NoBom);
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
        var path = Required(o, "file_path", "edit");
        var oldValue = Required(o, "old_string", "edit");
        var newValue = Required(o, "new_string", "edit");
        var (text, encoding) = ReadText(path);
        var replaced = IsTrue(o, "replace_all")
            ? text.Replace(oldValue, newValue, StringComparison.Ordinal)
            : ReplaceFirst(text, oldValue, newValue);
        return WriteChangedAsync(path, text, replaced, encoding, "edit");
    }

    private static Task<int> EditRegexAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        var path = Required(o, "file_path", "file_edit_regex");
        var pattern = Required(o, "pattern", "file_edit_regex");
        var replacement = Required(o, "replacement", "file_edit_regex");
        var (text, encoding) = ReadText(path);
        var regex = new Regex(pattern);
        var replaced = IsTrue(o, "replace_all", fallback: true)
            ? regex.Replace(text, replacement)
            : regex.Replace(text, replacement, 1);
        return WriteChangedAsync(path, text, replaced, encoding, "file_edit_regex");
    }

    private static Task<int> InsertLinesAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        var path = Required(o, "file_path", "file_insert_lines");
        var afterLine = GetInt(o, "after_line", 0);
        var newContent = Required(o, "new_content", "file_insert_lines");
        var (text, encoding) = ReadText(path);
        var lines = SplitLines(text).ToList();
        var index = Math.Clamp(afterLine, 0, lines.Count);
        lines.InsertRange(index, newContent.Split('\n'));
        WriteText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine, encoding);
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
        var (text, encoding) = ReadText(path);
        var lines = SplitLines(text).ToList();
        var count = Math.Min(end, lines.Count) - start + 1;
        if (count > 0) lines.RemoveRange(start - 1, count);
        WriteText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine, encoding);
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
            var (text, encoding) = ReadText(path);
            var replaced = IsTrue(o, "replace_all", fallback: true)
                ? text.Replace(oldValue, newValue, StringComparison.Ordinal)
                : ReplaceFirst(text, oldValue, newValue);
            if (string.Equals(text, replaced, StringComparison.Ordinal)) continue;
            WriteText(path, replaced, encoding);
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

    private static Task<int> WriteChangedAsync(string path, string before, string after, Encoding encoding, string toolId) {
        if (string.Equals(before, after, StringComparison.Ordinal)) {
            Console.Out.WriteLine($"{toolId}: 未找到可替换内容，文件未改动 ({path})");
            return Task.FromResult(0);
        }
        WriteText(path, after, encoding);
        Console.Out.WriteLine($"{toolId}: 已更新 {path}");
        return Task.FromResult(0);
    }

    private static string ReplaceFirst(string text, string oldValue, string newValue) {
        var index = text.IndexOf(oldValue, StringComparison.Ordinal);
        return index < 0 ? text : text[..index] + newValue + text[(index + oldValue.Length)..];
    }

    /// <summary>读文本并带回原文件编码 —— 编码决定写回时是否带 BOM（有签名保持签名，没有就没有）。</summary>
    private static (string Text, Encoding Encoding) ReadText(string path) {
        if (!global::System.IO.File.Exists(path))
            throw new ArgumentException($"文件不存在: {path}。正确写法: 先用 directory_list 确认路径。");
        return BclFileIO.Instance.DecodeBytes(global::System.IO.File.ReadAllBytes(path));
    }

    /// <summary>只探测签名（覆盖写时用）：读前 4 字节，不加载整份内容。</summary>
    private static Encoding DetectEncoding(string path) {
        using var stream = global::System.IO.File.OpenRead(path);
        var buffer = new byte[4];
        var read = stream.Read(buffer, 0, buffer.Length);
        return BclFileIO.DetectFromBOM(buffer.AsSpan(0, read));
    }

    private static void WriteText(string path, string content, Encoding encoding) =>
        global::System.IO.File.WriteAllBytes(path, BclFileIO.EncodeString(content, encoding));

    /// <summary>按 CRLF / LF / CR 切行，末尾换行不产生空行 —— 与 File.ReadAllLines 口径一致。</summary>
    private static string[] SplitLines(string text) {
        if (text.Length == 0) return [];
        var lines = new List<string>();
        var start = 0;
        for (var i = 0; i < text.Length; i++) {
            if (text[i] is not ('\r' or '\n')) continue;
            lines.Add(text[start..i]);
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            start = i + 1;
        }
        if (start < text.Length) lines.Add(text[start..]);
        return [.. lines];
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
