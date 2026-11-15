using System.Collections.Frozen;
using JoinCode.Abstractions.Tools;

namespace JoinCode.CliNode.Tools;

/// <summary>
/// tools 分组的本地命令表 — 只用 BCL，不引用引擎 / DI / 运行时会话 / LLM。
/// 本组 18 个工具中仅 2 个能用 BCL 诚实落地（纯文件批处理、测试回显），
/// 其余依赖引擎 DI / 运行时工具创建 / 调试会话 / LLM 的工具由 Program 转发宿主，exe 始终可用。
/// 参数名与 help.txt 的 [McpToolParameter] 逐一对应，说明不在此重复。
/// </summary>
internal static class ToolsCommands {
    private static readonly FrozenDictionary<string, CliNodeHandler> Handlers =
        new Dictionary<string, CliNodeHandler>(StringComparer.Ordinal) {
            ["skill_batch"] = BatchProcessAsync,
            ["test_tool"] = TestToolAsync
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static CliNodeHandler? Find(string toolId) => Handlers.TryGetValue(toolId, out var handler) ? handler : null;

    public static IEnumerable<string> LocalToolIds => Handlers.Keys.OrderBy(id => id, StringComparer.Ordinal);

    // ---- skill_batch: Batch process files（pattern + operation + search/replace + recursive）----

    private static Task<int> BatchProcessAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        var pattern = Required(o, "pattern", "skill_batch",
            "jcc-tool-tools.exe skill_batch --pattern \"*.cs\" --operation count");
        var operation = Required(o, "operation", "skill_batch",
            "jcc-tool-tools.exe skill_batch --pattern \"*.cs\" --operation search --search TODO");
        var normalized = pattern.Replace('\\', '/');
        var recursive = IsTrue(o, "recursive");
        if (normalized.Contains("**", StringComparison.Ordinal)) {
            recursive = true;
            normalized = normalized.Replace("**/", string.Empty, StringComparison.Ordinal)
                                   .Replace("/**", string.Empty, StringComparison.Ordinal)
                                   .Replace("**", "*", StringComparison.Ordinal);
        }
        var directory = global::System.IO.Path.GetDirectoryName(normalized) ?? ".";
        var mask = global::System.IO.Path.GetFileName(normalized) ?? "*";
        if (directory.Length == 0) directory = ".";
        if (mask.Length == 0) mask = "*";
        if (!global::System.IO.Directory.Exists(directory))
            throw Fail("skill_batch",
                $"--pattern 指向的目录不存在: {directory}",
                "--pattern",
                "jcc-tool-tools.exe skill_batch --pattern \"*.cs\" --operation count（先确认目录存在）");
        var option = recursive ? global::System.IO.SearchOption.AllDirectories : global::System.IO.SearchOption.TopDirectoryOnly;
        var files = global::System.IO.Directory.EnumerateFiles(directory, mask, option)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        return operation switch {
            "count" => CountAsync(files, o, ct),
            "search" => SearchAsync(files, o, ct),
            "replace" => ReplaceAsync(files, o, ct),
            "delete" => DeleteAsync(files, ct),
            _ => throw Fail("skill_batch",
                $"--operation 取值 `{operation}` 不在 help.txt 列举的 search/replace/delete/count 之内",
                "--operation",
                "jcc-tool-tools.exe skill_batch --pattern \"*.cs\" --operation count（四选一）")
        };
    }

    private static Task<int> CountAsync(string[] files, IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var search = Get(o, "search", string.Empty);
        if (search.Length == 0) {
            Console.Out.WriteLine($"skill_batch count: 匹配文件 {files.Length} 个");
            return Task.FromResult(0);
        }
        var hits = 0;
        foreach (var file in files) {
            ct.ThrowIfCancellationRequested();
            hits += global::System.IO.File.ReadLines(file)
                .Count(line => line.Contains(search, StringComparison.Ordinal));
        }
        Console.Out.WriteLine($"skill_batch count: 在 {files.Length} 个文件中命中 \"{search}\" 共 {hits} 行");
        return Task.FromResult(0);
    }

    private static Task<int> SearchAsync(string[] files, IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var search = RequiredWhen(o, "search", "skill_batch", "search",
            "jcc-tool-tools.exe skill_batch --pattern \"*.cs\" --operation search --search TODO");
        var total = 0;
        foreach (var file in files) {
            ct.ThrowIfCancellationRequested();
            var lineNo = 0;
            foreach (var line in global::System.IO.File.ReadLines(file)) {
                lineNo++;
                if (!line.Contains(search, StringComparison.Ordinal)) continue;
                Console.Out.WriteLine($"{file}:{lineNo}:{line}");
                total++;
            }
        }
        Console.Out.WriteLine($"skill_batch search: 命中 {total} 行");
        return Task.FromResult(0);
    }

    private static Task<int> ReplaceAsync(string[] files, IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var search = RequiredWhen(o, "search", "skill_batch", "replace",
            "jcc-tool-tools.exe skill_batch --pattern \"*.cs\" --operation replace --search OLD --replace NEW");
        var replacement = Get(o, "replace", string.Empty);
        var changed = 0;
        foreach (var file in files) {
            ct.ThrowIfCancellationRequested();
            var before = global::System.IO.File.ReadAllText(file);
            var after = before.Replace(search, replacement, StringComparison.Ordinal);
            if (string.Equals(before, after, StringComparison.Ordinal)) continue;
            global::System.IO.File.WriteAllText(file, after);
            changed++;
        }
        Console.Out.WriteLine($"skill_batch replace: 修改 {changed}/{files.Length} 个文件");
        return Task.FromResult(0);
    }

    private static Task<int> DeleteAsync(string[] files, CancellationToken ct) {
        foreach (var file in files) {
            ct.ThrowIfCancellationRequested();
            global::System.IO.File.Delete(file);
            Console.Out.WriteLine($"skill_batch delete: 已删除 {file}");
        }
        Console.Out.WriteLine($"skill_batch delete: 共删除 {files.Length} 个文件");
        return Task.FromResult(0);
    }

    // ---- test_tool: test（--arg 必填，确定性回显）----

    private static Task<int> TestToolAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        var arg = Required(o, "arg", "test_tool",
            "jcc-tool-tools.exe test_tool --arg \"hello\"");
        Console.Out.WriteLine($"test_tool: --arg = {arg}");
        return Task.FromResult(0);
    }

    // ---- 参数解析与 Rust 风格四要素引导 ----

    private static string Required(IReadOnlyDictionary<string, string> o, string key, string toolId, string example) =>
        o.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw Fail(toolId,
                $"help.txt 中 [McpToolParameter] 将 --{key} 标记为「必填」，本次命令行未提供或为空",
                $"--{key}",
                example);

    private static string RequiredWhen(IReadOnlyDictionary<string, string> o, string key, string toolId, string operation, string example) =>
        o.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)
            ? value
            : throw Fail(toolId,
                $"--operation {operation} 需要 --{key} 才能执行，但本次命令行未提供或为空",
                $"--{key}",
                example);

    private static ArgumentException Fail(string toolId, string why, string trigger, string fix) =>
        new ArgumentException(
            $"error: 工具 `{toolId}` 无法满足本地执行前提\n" +
            $"  --> 触发参数: {trigger}\n" +
            "  |\n" +
            $"  = 为什么失败: {why}\n" +
            "  = 哪层拦截: ToolsCommands 本地命令表 · 参数校验层（jcc-tool-tools.exe 本地优先执行路径）\n" +
            $"  = 正确写法: {fix}");

    private static string Get(IReadOnlyDictionary<string, string> o, string key, string fallback) =>
        o.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : fallback;

    private static bool IsTrue(IReadOnlyDictionary<string, string> o, string key) =>
        o.TryGetValue(key, out var value) && bool.TryParse(value, out var parsed) && parsed;
}
