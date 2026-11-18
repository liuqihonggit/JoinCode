






namespace JoinCode.CliNode.Search;

/// <summary>
/// search 分组的本地命令表 — 只依赖 BCL 与可选的 ripgrep 进程。
/// <para>本地实现 8 个: glob / search_files（文件名匹配）与 grep / search_text / search_code /
/// code_search / search_codebase / symbol_search（内容检索，优先调用 rg，缺失时退回内置扫描）。</para>
/// <para>仍转发宿主: code_index_*（需持久化 AST 索引库）、graph_*（需调用图存储）、
/// code_index_search_semantic / search_document（需向量嵌入模型）。这些是状态与模型依赖，不是漏写。</para>
/// </summary>
internal static class SearchCommands {
    /// <summary>与宿主 GlobMaxResultSizeChars 同量级的输出上限，防止一次搜索把上下文灌满。</summary>
    private const int MaxOutputChars = 60_000;
    private const int DefaultHeadLimit = 250;

    private static readonly FrozenDictionary<string, CliNodeHandler> Handlers =
        new Dictionary<string, CliNodeHandler>(StringComparer.Ordinal) {
            ["glob"] = GlobAsync,
            ["search_files"] = SearchFilesAsync,
            ["grep"] = GrepAsync,
            ["search_text"] = SearchTextAsync,
            ["search_code"] = SearchCodeAsync,
            ["code_search"] = CodeSearchAsync,
            ["search_codebase"] = SearchCodebaseAsync,
            ["symbol_search"] = SymbolSearchAsync
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static CliNodeHandler? Find(string toolId) => Handlers.TryGetValue(toolId, out var handler) ? handler : null;

    public static IEnumerable<string> LocalToolIds => Handlers.Keys.OrderBy(id => id, StringComparer.Ordinal);

    // ── glob 家族 ────────────────────────────────────────────────────────────

    private static Task<int> GlobAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var pattern = Required(o, "pattern", "glob");
        var root = ResolveDirectory(o.GetValueOrDefault("path"));
        return Task.FromResult(RunGlob(pattern, root));
    }

    private static Task<int> SearchFilesAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var pattern = Required(o, "pattern", "search_files");
        var root = ResolveDirectory(o.GetValueOrDefault("path"));
        return Task.FromResult(RunGlob(pattern, root));
    }

    private static int RunGlob(string pattern, string root) {
        var matcher = GlobMatcher.Create(pattern);
        var files = new List<(string Full, DateTime Modified)>();
        foreach (var file in WalkFiles(root, null)) {
            var relative = Path.GetRelativePath(root, file).Replace('\\', '/');
            if (!matcher.IsMatch(relative)) continue;
            files.Add((file, SafeModified(file)));
        }
        if (files.Count == 0) {
            Console.Out.WriteLine($"No files found matching pattern '{pattern}' in {root}");
            return 0;
        }
        files.Sort((a, b) => b.Modified.CompareTo(a.Modified));
        var output = new StringBuilder(files.Count * 48);
        var emitted = 0;
        foreach (var (full, _) in files) {
            var relative = Path.GetRelativePath(Environment.CurrentDirectory, full).Replace('\\', '/');
            if (relative.Length + output.Length + 1 > MaxOutputChars) break;
            output.AppendLine(relative);
            emitted++;
        }
        Console.Out.Write(output.ToString());
        if (emitted < files.Count) Console.Out.WriteLine($"[输出已截断: {emitted}/{files.Count} 项]");
        return 0;
    }

    // ── grep 家族 ────────────────────────────────────────────────────────────

    private static Task<int> GrepAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var query = new GrepQuery {
            Pattern = Required(o, "pattern", "grep"),
            Root = ResolveDirectory(o.GetValueOrDefault("path")),
            Glob = Nullable(o, "glob"),
            OutputMode = o.GetValueOrDefault("output_mode", "files_with_matches")!.Trim().ToLowerInvariant(),
            IgnoreCase = GetBool(o, "case_insensitive", false) || GetBool(o, "-i", false),
            Multiline = GetBool(o, "multiline", false),
            FileType = Nullable(o, "file_type") ?? Nullable(o, "type"),
            Before = GetIntOrNull(o, "before", "-B"),
            After = GetIntOrNull(o, "after", "-A"),
            Context = GetIntOrNull(o, "context", "-C"),
            LineNumbers = GetBool(o, "line_numbers", GetBool(o, "-n", true)),
            HeadLimit = GetIntOrNull(o, "head_limit") ?? DefaultHeadLimit,
            Offset = GetIntOrNull(o, "offset") ?? 0
        };
        return RunGrepAsync(query, ct);
    }

    private static Task<int> SearchTextAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var query = new GrepQuery {
            Pattern = Required(o, "pattern", "search_text"),
            Root = ResolveDirectory(o.GetValueOrDefault("path"))
        };
        return RunGrepAsync(query, ct);
    }

    private static Task<int> SearchCodeAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var query = new GrepQuery {
            Pattern = Required(o, "query", "search_code"),
            Root = ResolveDirectory(o.GetValueOrDefault("path")),
            FileType = null,
            // 与宿主一致: 只搜常见代码后缀，避免把日志/二进制当代码返回。
            IncludeExtensions = CodeExtensions
        };
        return RunGrepAsync(query, ct);
    }

    private static Task<int> CodeSearchAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var query = new GrepQuery {
            Pattern = Required(o, "query", "code_search"),
            Root = ResolveDirectory(o.GetValueOrDefault("path")),
            IncludeExtensions = CodeExtensions
        };
        return RunGrepAsync(query, ct);
    }

    private static Task<int> SearchCodebaseAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var query = new GrepQuery {
            Pattern = Required(o, "query", "search_codebase"),
            Root = ResolveDirectory(o.GetValueOrDefault("path"))
        };
        return RunGrepAsync(query, ct);
    }

    private static Task<int> SymbolSearchAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var symbol = Required(o, "symbol", "symbol_search");
        // 与宿主同一套推导: 关键字 + 词边界 + 允许前后缀（Main → MainAsync）。
        var query = new GrepQuery {
            Pattern = $@"\b(class|interface|struct|enum|void|public|private|protected|internal|static|async|Task|function|def|fn)\s+\w*{Regex.Escape(symbol)}\w*",
            Root = ResolveDirectory(o.GetValueOrDefault("path")),
            OutputMode = "content",
            LineNumbers = true
        };
        return RunGrepAsync(query, ct);
    }

    private static readonly string[] CodeExtensions = [
        ".cs", ".ts", ".tsx", ".js", ".jsx", ".py", ".go", ".rs", ".java", ".kt", ".cpp", ".c", ".h", ".csproj"
    ];

    private sealed class GrepQuery {
        public required string Pattern { get; init; }
        public required string Root { get; init; }
        public string? Glob { get; init; }
        public string OutputMode { get; init; } = "files_with_matches";
        public bool IgnoreCase { get; init; }
        public bool Multiline { get; init; }
        public string? FileType { get; init; }
        public int? Before { get; init; }
        public int? After { get; init; }
        public int? Context { get; init; }
        public bool LineNumbers { get; init; } = true;
        public int HeadLimit { get; init; } = DefaultHeadLimit;
        public int Offset { get; init; }
        public string[]? IncludeExtensions { get; init; }

        public int BeforeLines => Math.Max(Context ?? 0, Before ?? 0);
        public int AfterLines => Math.Max(Context ?? 0, After ?? 0);
    }

    private sealed record GrepHit(string File, int Line, string Text, bool IsContext);

    private sealed record GrepOutcome(List<GrepHit> Hits, List<string> Files, int Matches);

    private static async Task<int> RunGrepAsync(GrepQuery query, CancellationToken ct) {
        var rg = FindRipgrep();
        var outcome = rg is null
            ? ScanLocally(query, ct)
            : await ScanWithRipgrepAsync(rg, query, ct).ConfigureAwait(false);

        switch (query.OutputMode) {
            case "content":
                WriteContent(query, outcome);
                return 0;
            case "count":
                WriteCount(query, outcome);
                return 0;
            default:
                WriteFiles(query, outcome);
                return 0;
        }
    }

    private static void WriteContent(GrepQuery query, GrepOutcome outcome) {
        if (outcome.Hits.Count == 0) {
            Console.Out.WriteLine($"No matches found for pattern '{query.Pattern}' in {query.Root}");
            return;
        }
        var window = Slice(outcome.Hits, query.Offset, query.HeadLimit);
        var output = new StringBuilder(window.Count * 96);
        string? previous = null;
        foreach (var hit in window) {
            if (hit.File != previous) {
                if (previous is not null) output.AppendLine("--");
                previous = hit.File;
            }
            var relative = RelativeToCwd(hit.File);
            var separator = hit.IsContext ? '-' : ':';
            output.Append(relative).Append(separator);
            if (query.LineNumbers) output.Append(hit.Line).Append(separator);
            output.AppendLine(hit.Text);
            if (output.Length > MaxOutputChars) break;
        }
        Console.Out.Write(output.ToString());
        AppendPagination(query, window.Count, outcome.Hits.Count);
    }

    private static void WriteFiles(GrepQuery query, GrepOutcome outcome) {
        if (outcome.Files.Count == 0) {
            Console.Out.WriteLine($"No matches found for pattern '{query.Pattern}' in {query.Root}");
            return;
        }
        var window = Slice(outcome.Files, query.Offset, query.HeadLimit);
        Console.Out.WriteLine($"Found {outcome.Files.Count} files");
        foreach (var file in window) Console.Out.WriteLine(RelativeToCwd(file));
        AppendPagination(query, window.Count, outcome.Files.Count);
    }

    private static void WriteCount(GrepQuery query, GrepOutcome outcome) {
        if (outcome.Files.Count == 0) {
            Console.Out.WriteLine($"No matches found for pattern '{query.Pattern}' in {query.Root}");
            return;
        }
        var window = Slice(outcome.Files, query.Offset, query.HeadLimit);
        foreach (var file in window) {
            var count = outcome.Hits.Count(hit => !hit.IsContext && hit.File == file);
            Console.Out.WriteLine($"{RelativeToCwd(file)}:{count}");
        }
        var occurrenceWord = outcome.Matches == 1 ? "occurrence" : "occurrences";
        var fileWord = outcome.Files.Count == 1 ? "file" : "files";
        Console.Out.WriteLine();
        Console.Out.WriteLine($"Found {outcome.Matches} total {occurrenceWord} across {outcome.Files.Count} {fileWord}.");
        AppendPagination(query, window.Count, outcome.Files.Count);
    }

    private static void AppendPagination(GrepQuery query, int shown, int total) {
        if (shown >= total) return;
        var parts = new List<string>(2);
        if (query.HeadLimit > 0) parts.Add($"limit: {query.HeadLimit}");
        if (query.Offset > 0) parts.Add($"offset: {query.Offset}");
        if (parts.Count == 0) return;
        Console.Out.WriteLine();
        Console.Out.WriteLine($"[Showing results with pagination = {string.Join(", ", parts)}]");
    }

    private static List<T> Slice<T>(List<T> source, int offset, int limit) {
        var start = Math.Clamp(offset, 0, source.Count);
        var count = limit <= 0 ? source.Count - start : Math.Min(limit, source.Count - start);
        return source.GetRange(start, count);
    }

    // ── ripgrep 通道 ─────────────────────────────────────────────────────────

    private static string? FindRipgrep() {
        var configured = Environment.GetEnvironmentVariable("JCC_RG_EXE");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        var name = OperatingSystem.IsWindows() ? "rg.exe" : "rg";
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)) {
            var candidate = Path.Combine(directory.Trim().Trim('"'), name);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    private static async Task<GrepOutcome> ScanWithRipgrepAsync(string rg, GrepQuery query, CancellationToken ct) {
        var psi = new ProcessStartInfo(rg) {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("--json");
        psi.ArgumentList.Add("--no-config");
        psi.ArgumentList.Add("--max-columns");
        psi.ArgumentList.Add("1000");
        psi.ArgumentList.Add("--max-columns-preview");
        if (query.IgnoreCase) psi.ArgumentList.Add("--ignore-case");
        if (query.Multiline) {
            psi.ArgumentList.Add("--multiline");
            psi.ArgumentList.Add("--multiline-dotall");
        }
        if (query.BeforeLines > 0) {
            psi.ArgumentList.Add("--before-context");
            psi.ArgumentList.Add(query.BeforeLines.ToString());
        }
        if (query.AfterLines > 0) {
            psi.ArgumentList.Add("--after-context");
            psi.ArgumentList.Add(query.AfterLines.ToString());
        }
        if (!string.IsNullOrWhiteSpace(query.Glob)) {
            psi.ArgumentList.Add("--glob");
            psi.ArgumentList.Add(query.Glob);
        }
        if (!string.IsNullOrWhiteSpace(query.FileType)) {
            psi.ArgumentList.Add("--type");
            psi.ArgumentList.Add(query.FileType);
        }
        foreach (var extension in query.IncludeExtensions ?? []) {
            psi.ArgumentList.Add("--glob");
            psi.ArgumentList.Add("*" + extension);
        }
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add(query.Pattern);
        psi.ArgumentList.Add(query.Root);

        using var process = Process.Start(psi) ?? throw new IOException($"无法启动 ripgrep: {rg}");
        var hits = new List<GrepHit>();
        var files = new List<string>();
        var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var matches = 0;
        string? currentFile = null;

        var stderrTask = process.StandardError.ReadToEndAsync(ct);
        while (true) {
            var line = await process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false);
            if (line is null) break;
            if (line.Length == 0) continue;
            try {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var kind = root.TryGetProperty("type", out var type) ? type.GetString() : null;
                if (kind is "begin" or "end") {
                    if (kind == "begin" && root.TryGetProperty("data", out var data)
                        && data.TryGetProperty("path", out var pathElement)) {
                        currentFile = DecodePath(pathElement);
                        if (currentFile is not null && seenFiles.Add(currentFile)) files.Add(currentFile);
                    }
                    continue;
                }
                if (kind is not ("match" or "context")) continue;
                var payload = root.GetProperty("data");
                var file = DecodePath(payload.GetProperty("path")) ?? currentFile;
                if (file is null) continue;
                if (seenFiles.Add(file)) files.Add(file);
                var text = payload.TryGetProperty("lines", out var lines) && lines.TryGetProperty("text", out var textNode)
                    ? textNode.GetString() ?? string.Empty
                    : string.Empty;
                var number = payload.TryGetProperty("line_number", out var numberNode) && numberNode.ValueKind == JsonValueKind.Number
                    ? numberNode.GetInt32()
                    : 0;
                if (kind == "match") {
                    matches++;
                    hits.Add(new GrepHit(file, number, Trim(text), false));
                } else {
                    hits.Add(new GrepHit(file, number, Trim(text), true));
                }
            } catch (JsonException) {
                // 单行 JSON 异常不应中断整次搜索，跳过该行继续读。
            }
        }
        await process.WaitForExitAsync(ct).ConfigureAwait(false);
        var stderr = await stderrTask.ConfigureAwait(false);
        // rg 退出码: 0 有匹配 / 1 无匹配 / 2 出错；2 才需要报错。
        if (process.ExitCode >= 2 && !string.IsNullOrWhiteSpace(stderr))
            throw new IOException($"ripgrep 执行失败（exit={process.ExitCode}）：{stderr.Trim()}");
        return new GrepOutcome(hits, files, matches);
    }

    private static string Trim(string text) => text.TrimEnd('\r', '\n');

    /// <summary>rg --json 的 path 可能是 text 或 bytes 形式，两种都要能取到。</summary>
    private static string? DecodePath(JsonElement element) {
        if (element.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            return text.GetString();
        if (element.TryGetProperty("bytes", out var bytes) && bytes.ValueKind == JsonValueKind.String) {
            try {
                return Encoding.UTF8.GetString(Convert.FromBase64String(bytes.GetString() ?? string.Empty));
            } catch (FormatException) {
                return null;
            }
        }
        return null;
    }

    // ── 内置扫描通道（rg 缺失时） ────────────────────────────────────────────

    private static GrepOutcome ScanLocally(GrepQuery query, CancellationToken ct) {
        var options = RegexOptions.CultureInvariant;
        if (query.IgnoreCase) options |= RegexOptions.IgnoreCase;
        if (query.Multiline) options |= RegexOptions.Singleline;
        var regex = new Regex(query.Pattern, options);

        var include = query.IncludeExtensions ?? TypeExtensions(query.FileType);
        var globMatcher = string.IsNullOrWhiteSpace(query.Glob) ? null : GlobMatcher.Create(query.Glob);
        var hits = new List<GrepHit>();
        var files = new List<string>();
        var matches = 0;

        foreach (var file in WalkFiles(query.Root, include)) {
            ct.ThrowIfCancellationRequested();
            if (globMatcher is not null && !globMatcher.IsMatch(Path.GetRelativePath(query.Root, file))) continue;
            string[] lines;
            try {
                lines = File.ReadAllLines(file);
            } catch (IOException) {
                continue;
            } catch (UnauthorizedAccessException) {
                continue;
            }
            var fileHit = false;
            for (var i = 0; i < lines.Length; i++) {
                if (!regex.IsMatch(lines[i])) continue;
                fileHit = true;
                matches++;
                var from = Math.Max(0, i - query.BeforeLines);
                var to = Math.Min(lines.Length - 1, i + query.AfterLines);
                for (var j = from; j < i; j++) hits.Add(new GrepHit(file, j + 1, lines[j], true));
                hits.Add(new GrepHit(file, i + 1, lines[i], false));
                for (var j = i + 1; j <= to; j++) hits.Add(new GrepHit(file, j + 1, lines[j], true));
                i = to;
            }
            if (fileHit) files.Add(file);
        }
        return new GrepOutcome(hits, files, matches);
    }

    // ── 文件遍历与忽略规则 ───────────────────────────────────────────────────

    /// <summary>rg --type 在无 rg 环境下的最小等价映射；未登记的类型返回 null（不额外过滤）。</summary>
    private static string[]? TypeExtensions(string? fileType) => fileType?.Trim().ToLowerInvariant() switch {
        "js" => [".js", ".jsx", ".mjs", ".cjs"],
        "ts" => [".ts", ".tsx", ".mts", ".cts"],
        "py" => [".py", ".pyi"],
        "rust" => [".rs"],
        "go" => [".go"],
        "java" => [".java"],
        "cs" => [".cs", ".csproj", ".props", ".targets"],
        "cpp" => [".cpp", ".cc", ".cxx", ".hpp", ".h"],
        "c" => [".c", ".h"],
        "json" => [".json", ".jsonc"],
        "md" => [".md", ".markdown"],
        "yaml" => [".yml", ".yaml"],
        "html" => [".html", ".htm"],
        "css" => [".css", ".scss", ".less"],
        "sh" => [".sh", ".bash"],
        "ps1" => [".ps1", ".psm1"],
        _ => null
    };

    /// <summary>
    /// 构建产物与版本库元数据一律不搜：仓库里 artifacts/ 有上万个生成文件，
    /// 放进来会让 glob/grep 结果全是噪音（宿主靠 .gitignore 达到同样效果）。
    /// </summary>
    private static readonly FrozenSet<string> IgnoredDirectories = new[] {
        ".git", ".vs", ".vscode", ".idea", "node_modules", "bin", "obj", "artifacts",
        "packages", "dist", "build", "target", "__pycache__", ".workbuddy", ".xxx"
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<string> WalkFiles(string root, string[]? includeExtensions) {
        var extensions = includeExtensions is null
            ? null
            : new HashSet<string>(includeExtensions, StringComparer.OrdinalIgnoreCase);
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0) {
            var directory = pending.Pop();
            string[] children;
            try {
                children = Directory.GetDirectories(directory);
            } catch (IOException) {
                continue;
            } catch (UnauthorizedAccessException) {
                continue;
            }
            foreach (var child in children) {
                var name = Path.GetFileName(child);
                if (IgnoredDirectories.Contains(name)) continue;
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) != 0) continue;
                pending.Push(child);
            }
            string[] files;
            try {
                files = Directory.GetFiles(directory);
            } catch (IOException) {
                continue;
            } catch (UnauthorizedAccessException) {
                continue;
            }
            foreach (var file in files) {
                if (extensions is not null && !extensions.Contains(Path.GetExtension(file))) continue;
                yield return file;
            }
        }
    }

    private static DateTime SafeModified(string path) {
        try {
            return File.GetLastWriteTimeUtc(path);
        } catch (IOException) {
            return DateTime.MinValue;
        }
    }

    // ── 参数与路径 ───────────────────────────────────────────────────────────

    private static string ResolveDirectory(string? path) {
        var target = string.IsNullOrWhiteSpace(path) ? Environment.CurrentDirectory : path.Trim();
        if (target is "undefined" or "null") target = Environment.CurrentDirectory;
        var full = Path.GetFullPath(target);
        if (File.Exists(full)) return Path.GetDirectoryName(full) ?? Environment.CurrentDirectory;
        if (!Directory.Exists(full))
            throw new ArgumentException($"目录不存在: {path}。当前工作目录是 {Environment.CurrentDirectory}，请用 --path 指定真实目录。");
        return full;
    }

    private static string RelativeToCwd(string path) {
        var relative = Path.GetRelativePath(Environment.CurrentDirectory, path).Replace('\\', '/');
        return relative.StartsWith("..", StringComparison.Ordinal) ? path.Replace('\\', '/') : relative;
    }

    private static string Required(IReadOnlyDictionary<string, string> o, string key, string toolId) {
        if (o.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value)) return value;
        throw new ArgumentException($"工具 '{toolId}' 缺少必填参数 --{key}。正确写法: {toolId} -h 查看参数。");
    }

    private static string? Nullable(IReadOnlyDictionary<string, string> o, string key) =>
        o.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value : null;

    private static int? GetIntOrNull(IReadOnlyDictionary<string, string> o, params string[] keys) {
        foreach (var key in keys) {
            if (!o.TryGetValue(key, out var raw) || string.IsNullOrWhiteSpace(raw)) continue;
            if (int.TryParse(raw, out var value)) return value;
            throw new ArgumentException($"参数 --{key} 需要整数，当前值 '{raw}'。");
        }
        return null;
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
