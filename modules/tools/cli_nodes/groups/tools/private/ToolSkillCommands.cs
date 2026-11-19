// tools 组下沉 handler — 6 个纯 BCL 技能工具 + skill_list（文件扫描）。
// skill_stuck/simplify/debug/verify 是纯字符串启发式 + BCL 文件/Process；
// tool_list/show_template 读 ~/.jcc/tool-templates/*.json；skill_list 扫描 ~/.jcc/skills 与外部 SKILL.md。
// skill（对话注入/子智能体）、skill_execute（LLM 引擎+工具网关）、skill_search/recommend/discover
// （需宿主技能注册表含 MCP 远程技能）、tool_score*（宿主内存双写）保留 host。




namespace JoinCode.CliNode.Tools;

internal static partial class ToolsCommands {
    // ==================== skill_stuck（零依赖硬编码建议表） ====================

    private static Task<int> SkillStuckAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var approach = Get(o, "current_approach", "");
        var obstacle = Get(o, "obstacle", "");
        var goal = Get(o, "goal", "");
        var suggestions = new List<(string title, string desc)> {
            ("Try a different approach", "If current approach doesn't work, consider alternative algorithms or techniques"),
            ("Simplify the problem", "Create a minimal reproducible example to isolate from complex factors"),
            ("Review documentation", "Re-read relevant docs, may have missed important details or config"),
            ("Take a break", "Step away temporarily, may gain new perspective on return"),
            ("Seek help", "Describe the problem to peers or community, explaining may reveal insights"),
            ("Check assumptions", "List all assumptions, verify each one"),
            ("Revert to last working version", "If it worked before, compare diffs to find the issue")
        };
        var priority = new List<(string, string)>();
        if (Has(obstacle, "permission", "权限")) priority.Add(("Check permissions", "Confirm current user has sufficient permissions"));
        if (Has(obstacle, "network", "网络")) priority.Add(("Check network", "Verify network connection, try proxy or VPN"));
        if (Has(obstacle, "memory", "内存")) priority.Add(("Optimize memory usage", "Check for memory leaks, consider batch processing for large data"));
        if (priority.Count > 0) suggestions = priority.Concat(suggestions).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("✗ Stuck Help");
        sb.AppendLine();
        if (approach.Length > 0) sb.AppendLine($"Current approach: {approach}");
        if (obstacle.Length > 0) sb.AppendLine($"Obstacle: {obstacle}");
        if (goal.Length > 0) sb.AppendLine($"Goal: {goal}");
        sb.AppendLine();
        sb.AppendLine("◆ Suggested alternatives:");
        for (var i = 0; i < suggestions.Count; i++) sb.AppendLine($"  {i + 1}. {suggestions[i].title}: {suggestions[i].desc}");
        sb.AppendLine();
        sb.AppendLine("♻ Debugging strategies:");
        sb.AppendLine("  1. Break the problem into smaller verifiable steps");
        sb.AppendLine("  2. Add logging to observe intermediate state");
        sb.AppendLine("  3. Search for similar problems and known solutions");
        sb.AppendLine("  4. Verify the environment and dependencies");
        Console.Out.Write(sb.ToString());
        return Task.FromResult(0);
    }

    // ==================== skill_simplify（字符串启发式） ====================

    private static Task<int> SkillSimplifyAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var path = Required(o, "file_path", "skill_simplify");
        var type = Get(o, "simplify_type", "all");
        if (!File.Exists(path)) throw new ArgumentException($"skill_simplify 文件不存在: {path}");
        var lines = File.ReadAllLines(path);
        var suggestions = new List<(string cat, int line, string text)>();
        for (var i = 0; i < lines.Length; i++) {
            var line = lines[i];
            if (line.Length > 120 && (type == "all" || type == "readability"))
                suggestions.Add(("readability", i + 1, $"Line too long ({line.Length} chars), consider splitting"));
            var ops = CountOf(line, '&') + CountOf(line, '|');
            if (ops > 2 && (type == "all" || type == "complexity"))
                suggestions.Add(("complexity", i + 1, $"Complex boolean expression ({ops} operators), consider extracting a method"));
            var indent = line.Length - line.TrimStart().Length;
            if (indent > 16 && (type == "all" || type == "readability"))
                suggestions.Add(("readability", i + 1, $"Deep indentation ({indent} spaces), consider early return"));
            if (line.Contains('+') && line.Contains('"') && (type == "all" || type == "performance"))
                suggestions.Add(("performance", i + 1, "String concatenation in loop-like context, consider StringBuilder"));
        }
        var sb = new StringBuilder();
        sb.AppendLine("⚙ Code Simplification Analysis");
        sb.AppendLine($"File: {path}");
        sb.AppendLine($"Type: {type}");
        sb.AppendLine();
        if (suggestions.Count == 0) sb.AppendLine("✓ Code already looks clean! No obvious simplification opportunities found.");
        else {
            sb.AppendLine($"Found {suggestions.Count} simplification suggestions:");
            sb.AppendLine();
            for (var i = 0; i < suggestions.Count; i++) sb.AppendLine($"{i + 1}. [{suggestions[i].cat}] Line {suggestions[i].line}\n   {suggestions[i].text}\n");
        }
        Console.Out.Write(sb.ToString());
        return Task.FromResult(0);
    }

    // ==================== skill_debug（文件存在性 + 错误关键词） ====================

    private static Task<int> SkillDebugAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var path = Get(o, "path", Directory.GetCurrentDirectory());
        var error = Get(o, "error_message", "");
        var type = Get(o, "debug_type", "all");
        var sb = new StringBuilder();
        sb.AppendLine("✎ Debug Diagnostics");
        sb.AppendLine();
        sb.AppendLine("Error message:");
        sb.AppendLine("```");
        sb.AppendLine(error);
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("🔍 Error analysis:");
        if (Has(error, "null", "空引用", "nullreference")) sb.AppendLine("  - Likely a null reference; check null guards before dereferencing.");
        if (Has(error, "timeout", "超时")) sb.AppendLine("  - Timeout detected; check I/O latency and configured timeouts.");
        if (Has(error, "permission", "权限", "access denied")) sb.AppendLine("  - Access denied; check file/system permissions.");
        if (Has(error, "not found", "找不到", "不存在")) sb.AppendLine("  - Missing resource; verify the path/resource exists.");
        sb.AppendLine();
        sb.AppendLine("◆ Diagnostic suggestions:");
        sb.AppendLine("  - Check the stack trace for the exact failing frame");
        sb.AppendLine("  - Reproduce with a minimal input");
        sb.AppendLine("  - Compare against the last working configuration");
        sb.AppendLine();
        sb.AppendLine("⚙ Debugging steps:");
        string[] checks = ["*.log", "appsettings.json", "web.config", ".env", "package.json", "*.csproj"];
        sb.AppendLine("  1. Inspect nearby config/log files: " + string.Join(", ", checks.Where(c =>
            Directory.Exists(path) && (Directory.GetFiles(path, c).Length > 0 || File.Exists(Path.Combine(path, c))))));
        sb.AppendLine("  2. Add targeted logging around the failure point");
        sb.AppendLine("  3. Run with verbose diagnostics enabled");
        sb.AppendLine("  4. Verify dependencies and environment variables");
        Console.Out.Write(sb.ToString());
        return Task.FromResult(0);
    }

    // ==================== skill_verify（Process 编排） ====================

    private static async Task<int> SkillVerifyAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var path = Required(o, "path", "skill_verify");
        var type = Get(o, "verify_type", "all");
        var filter = Get(o, "test_filter", "");
        var checks = new List<(string name, bool ok, string msg)>();
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        if (type is "syntax" or "all") {
            var ext = Path.GetExtension(path).ToLowerInvariant();
            var (exe, args) = ext switch {
                ".cs" => ("dotnet", new[] { "build", "--verbosity", "quiet", "--no-restore" }),
                ".py" => ("python", new[] { "-m", "py_compile", path }),
                ".js" or ".ts" => ("node", new[] { "--check", path }),
                _ => ("", Array.Empty<string>())
            };
            if (exe.Length > 0) {
                var r = await RunProcAsync(exe, args, ext == ".cs" ? Path.GetDirectoryName(Path.GetFullPath(path)) : Directory.GetCurrentDirectory(), ct).ConfigureAwait(false);
                checks.Add(("syntax", r.exit == 0, r.exit == 0 ? $"{ext} syntax OK" : r.err.Trim()));
            }
        }
        if (type is "build" or "all") {
            var r = await RunProcAsync("dotnet", ["build", "--verbosity", "quiet", "--no-restore"], Directory.GetCurrentDirectory(), ct).ConfigureAwait(false);
            checks.Add(("build", r.exit == 0, r.exit == 0 ? "build OK" : r.err.Trim()));
        }
        if (type is "test" or "all") {
            var args = new List<string> { "test", "--verbosity", "quiet" };
            if (filter.Length > 0) { args.Add("--filter"); args.Add(filter); }
            var r = await RunProcAsync("dotnet", args.ToArray(), Directory.GetCurrentDirectory(), ct).ConfigureAwait(false);
            checks.Add(("test", r.exit == 0, r.exit == 0 ? "tests OK" : r.err.Trim()));
        }
        var sb = new StringBuilder();
        sb.AppendLine("✓ Code Verification");
        sb.AppendLine($"Path: {path}");
        sb.AppendLine();
        var passed = checks.Count(c => c.ok);
        sb.AppendLine($"Check results: {passed}/{checks.Count} passed");
        sb.AppendLine();
        foreach (var c in checks) sb.AppendLine($"  {(c.ok ? "✓" : "✗")} {c.name}: {c.msg}");
        Console.Out.Write(sb.ToString());
        return 0;
    }

    // ==================== tool_list / show_template（文件读取） ====================

    private static string TemplateDir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jcc", "tool-templates");

    private static Task<int> ToolListTemplatesAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        if (!Directory.Exists(TemplateDir)) { Console.Out.WriteLine("暂无工具模板。使用 tool_create 创建新工具。"); return Task.FromResult(0); }
        var files = Directory.GetFiles(TemplateDir, "*.json");
        if (files.Length == 0) { Console.Out.WriteLine("暂无工具模板。使用 tool_create 创建新工具。"); return Task.FromResult(0); }
        Console.Out.WriteLine($"共 {files.Length} 个工具模板：");
        foreach (var f in files) {
            var json = File.ReadAllText(f);
            string name = Path.GetFileNameWithoutExtension(f), desc = "", type = "";
            try {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("toolName", out var n)) name = n.GetString() ?? name;
                if (root.TryGetProperty("description", out var d)) desc = d.GetString() ?? "";
                if (root.TryGetProperty("executionType", out var t)) type = t.GetString() ?? "";
            } catch (System.Text.Json.JsonException) { /* 用文件名兜底 */ }
            Console.Out.WriteLine($"- {name}: {desc} ({type})");
        }
        return Task.FromResult(0);
    }

    private static Task<int> ToolShowTemplateAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var id = Required(o, "templateId", "tool_show_template");
        var path = Path.Combine(TemplateDir, id + ".json");
        var found = File.Exists(path) ? path : Directory.Exists(TemplateDir)
            ? Directory.GetFiles(TemplateDir, "*.json").FirstOrDefault(f => {
                try { using var d = System.Text.Json.JsonDocument.Parse(File.ReadAllText(f));
                    return d.RootElement.TryGetProperty("id", out var v) && v.GetString() == id; } catch { return false; }
            }) : null;
        if (found is null) { Console.Out.WriteLine($"未找到工具模板: {id}"); return Task.FromResult(0); }
        using var doc = System.Text.Json.JsonDocument.Parse(File.ReadAllText(found));
        var root = doc.RootElement;
        string G(string k) => root.TryGetProperty(k, out var v) ? v.GetString() ?? "" : "";
        var sb = new StringBuilder();
        sb.AppendLine($"## 工具模板: {G("toolName")}");
        sb.AppendLine($"ID: {G("id")}");
        sb.AppendLine($"描述: {G("description")}");
        sb.AppendLine($"类型: {G("executionType")}");
        sb.AppendLine($"分组: {G("groupName")}");
        sb.AppendLine($"命令: {G("command")}");
        sb.AppendLine();
        sb.AppendLine("### 参数:");
        if (root.TryGetProperty("parameters", out var ps) && ps.ValueKind == System.Text.Json.JsonValueKind.Array)
            foreach (var p in ps.EnumerateArray()) {
                var pn = p.TryGetProperty("name", out var nv) ? nv.GetString() : "?";
                var pt = p.TryGetProperty("type", out var tv) ? tv.GetString() : "?";
                var req = p.TryGetProperty("required", out var rv) && rv.GetBoolean() ? "必填" : "可选";
                var pd = p.TryGetProperty("description", out var dv) ? dv.GetString() : "";
                sb.AppendLine($"  {pn} ({pt}, {req}): {pd}");
            }
        Console.Out.Write(sb.ToString());
        return Task.FromResult(0);
    }

    // ==================== skill_list（扫描文件技能） ====================

    private static Task<int> SkillListAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var skills = new List<(string name, string desc, string tag)>();
        var skillsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".jcc", "skills");
        if (Directory.Exists(skillsDir)) {
            foreach (var f in Directory.EnumerateFiles(skillsDir, "SKILL.md", SearchOption.AllDirectories)) {
                var text = File.ReadAllText(f);
                var name = Path.GetFileName(Path.GetDirectoryName(f)) ?? Path.GetFileNameWithoutExtension(f);
                var desc = ExtractFrontmatter(text, "description");
                var tag = ExtractFrontmatter(text, "tags");
                skills.Add((name, desc, tag));
            }
            foreach (var f in Directory.EnumerateFiles(skillsDir, "*.json", SearchOption.AllDirectories)) {
                try {
                    using var d = System.Text.Json.JsonDocument.Parse(File.ReadAllText(f));
                    var n = d.RootElement.TryGetProperty("name", out var nv) ? nv.GetString() : Path.GetFileNameWithoutExtension(f);
                    var desc = d.RootElement.TryGetProperty("description", out var dv) ? dv.GetString() : "";
                    var tag = d.RootElement.TryGetProperty("tags", out var tv) ? tv.ToString() : "";
                    skills.Add((n ?? "", desc ?? "", tag ?? ""));
                } catch (System.Text.Json.JsonException) { /* 跳过非法 JSON */ }
            }
        }
        var sb = new StringBuilder();
        sb.AppendLine($"Available skills ({skills.Count}):");
        foreach (var group in skills.GroupBy(s => string.IsNullOrEmpty(s.tag) ? "general" : s.tag).OrderBy(g => g.Key)) {
            sb.AppendLine($"[{group.Key}]");
            foreach (var s in group) sb.AppendLine($"  {s.name} - {s.desc}");
        }
        if (skills.Count == 0) sb.AppendLine("  (未发现外部技能文件；内置技能与 MCP 远程技能由宿主 jcc 提供)");
        Console.Out.Write(sb.ToString());
        return Task.FromResult(0);
    }

    private static string ExtractFrontmatter(string text, string key) {
        foreach (var line in text.Split('\n').Take(40)) {
            var t = line.Trim();
            if (t.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase)) return t[(key.Length + 1)..].Trim();
        }
        return "";
    }

    // ==================== 辅助 ====================

    private static async Task<(int exit, string err)> RunProcAsync(string exe, string[] args, string workDir, CancellationToken ct) {
        try {
            var psi = new ProcessStartInfo(exe) { WorkingDirectory = workDir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMinutes(2));
            var proc = Process.Start(psi);
            if (proc is null) return (-1, $"无法启动 {exe}");
            using var p = proc;
            var so = p.StandardOutput.ReadToEndAsync(); var se = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return (p.ExitCode, await se.ConfigureAwait(false));
        } catch (Exception ex) { return (-1, ex.Message); }
    }
    private static bool Has(string s, params string[] keys) => keys.Any(k => s.Contains(k, StringComparison.OrdinalIgnoreCase));
    private static int CountOf(string s, char c) { var n = 0; foreach (var ch in s) if (ch == c) n++; return n; }
    private static string Required(IReadOnlyDictionary<string, string> o, string key, string tool) =>
        o.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : throw new ArgumentException($"{tool} 缺少必填参数 --{key}。正确写法: jcc tool call tools {tool} --{key} <值>");
}
