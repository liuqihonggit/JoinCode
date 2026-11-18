// system 组下沉 handler — 错误诊断/permission 读写/model_search/policy_list/通知交互/brief。
// config 读写一律在宿主（键清单与存储源映射都在宿主侧，见下方 config 段落说明）。
// 全部只用 BCL + Win32 + HttpClient。注意分界：
// - permission 写类已下沉：规则文件 {gitRoot}/.jcc/permission/rules.json 是唯一真相源，
//   宿主 AgentPermissionManager 按内容戳感知外部改写（见 AgentPermissionManagerExternalWriteTests）。
// - config 读写（config_get/config_set/config_list）留宿主：键清单与「键 → 存储源」映射都是宿主唯一数据源。
// - 其余写类（brief_mode/memory 写/cron 写）宿主读的是进程内快照或只在启动装载，仍需宿主配套改造，留宿主。
// - agent_*/analytics_*/TUI(ask_user/confirm)/watcher(memory_team_sync) 绑定宿主进程内状态，物理边界，留宿主。







namespace JoinCode.CliNode.System;

internal static partial class SystemCommands {
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static string Jcc => Path.Combine(Home, ".jcc");

    // config_get / config_list 已退休回宿主（2026-11-18，TASK089）：
    // exe 版自带一份 supported 键清单 + 「先 settings 再 global」的递归找键，与宿主 SupportedSettings +
    // SettingSourceMap 各成一套，真机上 config_set 写 global、config_get 报未设置。
    // 本文件保留 config 相关的**写**语义说明，读一律走宿主，见 SystemCommands.Handlers 注释。

    // ==================== 错误诊断/修复（确定性规则） ====================

    private static Task<int> DiagnoseErrorAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var tool = Get(o, "failed_tool_name", "unknown");
        var error = Required(o, "error_message", "diagnose_error");
        var sb = new StringBuilder();
        sb.AppendLine($"## 工具 '{tool}' 执行失败诊断");
        sb.AppendLine();
        sb.AppendLine($"**错误信息**: {error}");
        sb.AppendLine();
        sb.AppendLine("### 常见原因分析");
        if (Has(error, "permission", "denied", "权限")) sb.AppendLine("- **权限不足**: 检查文件/目录权限（chmod/icacls）");
        if (Has(error, "not found", "no such file", "找不到", "不存在")) sb.AppendLine("- **路径不存在**: 检查路径拼写与工作目录");
        if (Has(error, "timeout", "超时")) sb.AppendLine("- **超时**: 检查 I/O 延迟与超时配置");
        if (Has(error, "connection", "network", "网络")) sb.AppendLine("- **网络问题**: 检查连接、代理与防火墙");
        if (Has(error, "syntax", "parse", "语法")) sb.AppendLine("- **语法错误**: 检查输入格式与转义");
        sb.AppendLine("- 建议: 查看完整堆栈定位失败帧，并用最小输入复现");
        Console.Out.Write(sb.ToString());
        return Task.FromResult(0);
    }

    private static Task<int> FixShellErrorAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var cmd = Get(o, "failed_command", "");
        var exit = Get(o, "exit_code", "");
        var err = Get(o, "error_output", "");
        var sb = new StringBuilder();
        sb.AppendLine("## Shell 命令失败修复建议");
        sb.AppendLine();
        sb.AppendLine($"**失败命令**: `{cmd}`");
        sb.AppendLine($"**退出码**: {exit}");
        sb.AppendLine($"**错误输出**: {err}");
        sb.AppendLine();
        sb.AppendLine("### 替代方案");
        if (Has(err, "not found", "command not found")) sb.AppendLine("- 命令不存在：确认已安装并在 PATH 中，或换用等价命令");
        if (Has(err, "permission", "denied")) sb.AppendLine("- 权限不足：用管理员权限运行，或修改目标权限");
        if (Has(err, "directory", "目录")) sb.AppendLine("- 目录问题：确认工作目录存在（--working_directory）");
        sb.AppendLine("- 用 `;` 分隔步骤以定位失败点，或先用只读命令验证环境");
        Console.Out.Write(sb.ToString());
        return Task.FromResult(0);
    }

    private static Task<int> FixFileErrorAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var path = Required(o, "file_path", "fix_file_error");
        var sb = new StringBuilder();
        sb.AppendLine("## 文件错误修复建议");
        sb.AppendLine($"路径: {path}");
        sb.AppendLine($"存在: {(File.Exists(path) ? "是" : "否")}");
        var dir = Path.GetDirectoryName(Path.GetFullPath(path))!;
        sb.AppendLine($"父目录存在: {(Directory.Exists(dir) ? "是" : "否")}");
        if (File.Exists(path)) sb.AppendLine($"大小: {new FileInfo(path).Length} 字节，只读: {new FileInfo(path).IsReadOnly}");
        sb.AppendLine();
        sb.AppendLine("建议: 确认路径拼写与大小写；只读文件先取消只读；编码问题尝试 UTF-8；权限问题检查 icacls/chmod。");
        Console.Out.Write(sb.ToString());
        return Task.FromResult(0);
    }

    private static async Task<int> FixMergeConflictAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var path = Get(o, "file_path", ".");
        var (exit, stdout, _) = await RunProcCaptureAsync("git", ["branch", "--show-current"], Directory.Exists(path) ? path : Directory.GetCurrentDirectory(), ct).ConfigureAwait(false);
        var conflicts = Directory.Exists(path)
            ? Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories).Where(f => {
                try { return File.ReadAllText(f).Contains("<<<<<<<", StringComparison.Ordinal); } catch { return false; }
            }).Take(20).ToList()
            : [];
        var sb = new StringBuilder();
        sb.AppendLine("## 合并冲突诊断");
        sb.AppendLine($"分支: {(exit == 0 ? stdout.Trim() : "未知")}");
        sb.AppendLine($"检测到冲突标记的文件: {conflicts.Count}");
        foreach (var f in conflicts) sb.AppendLine($"  - {f}");
        sb.AppendLine();
        sb.AppendLine("建议: 逐个打开文件解决 <<<<<<< / ======= / >>>>>>> 标记，然后 git add 标记已解决。");
        Console.Out.Write(sb.ToString());
        return 0;
    }

    // ==================== permission（读写 rule 文件，文件是唯一真相源） ====================

    /// <summary>PermissionMode 声明序 — 数值与宿主 PermissionJsonContext 落盘结果一致。</summary>
    private static readonly string[] PermissionModeNames = ["plan", "auto", "ask", "bypass", "unattended"];

    /// <summary>PermissionLevel 声明序 — 数值与宿主 PermissionJsonContext 落盘结果一致。</summary>
    private static readonly string[] PermissionLevelNames = ["none", "read", "write", "execute", "admin"];

    /// <summary>向上找 .git 得工作区根 — 与宿主 GitWorkspaceResolver.FindGitWorkspaceDir 同语义。</summary>
    private static string? FindWorkspaceRoot() {
        for (var dir = new DirectoryInfo(Directory.GetCurrentDirectory()); dir is not null; dir = dir.Parent) {
            var git = Path.Combine(dir.FullName, ".git");
            if (Directory.Exists(git) || File.Exists(git)) return dir.FullName;
        }
        return null;
    }

    /// <summary>
    /// 宿主与 exe 共用的规则文件位置：{gitRoot}/.jcc/permission/rules.json。
    /// 不在 git 工作区内返回 null — 宿主此时同样不加载任何规则，这里保持同一语义。
    /// </summary>
    private static string? RulesPathOrNull() => FindWorkspaceRoot() is { } root
        ? Path.Combine(root, ".jcc", "permission", "rules.json")
        : null;

    private static JsonArray LoadRuleNodes() {
        var path = RulesPathOrNull();
        if (path is null || !File.Exists(path)) return [];
        try {
            return JsonNode.Parse(File.ReadAllText(path).TrimStart('\uFEFF')) as JsonArray ?? [];
        } catch (JsonException) {
            return [];
        }
    }

    private static void SaveRuleNodes(JsonArray rules) {
        var path = RulesPathOrNull()
            ?? throw new InvalidOperationException("当前目录不在 git 工作区内，规则文件无处落脚（宿主此时也不会加载规则）。请先 git init 或在仓库内执行。");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, rules.ToJsonString());
    }

    private static string Str(JsonNode? node) {
        if (node is not JsonValue value) return "";
        return value.TryGetValue<string>(out var s) ? s : value.ToJsonString();
    }

    /// <summary>模式/级别读数：宿主落盘是枚举数值，手写文件可能是字符串，两种都认。</summary>
    private static string EnumName(JsonNode? node, string[] names, string fallback) {
        if (node is not JsonValue value) return fallback;
        if (value.TryGetValue<int>(out var index)) return index >= 0 && index < names.Length ? names[index] : index.ToString();
        return value.TryGetValue<string>(out var s) ? s : fallback;
    }

    private static string[] StringArray(JsonNode? node) => node is JsonArray array
        ? [.. array.Select(Str).Where(s => s.Length > 0)]
        : [];

    private sealed record PermRuleView(string AgentPattern, string Mode, string Level, string[] Allowed, string[] Denied);

    private static List<PermRuleView> LoadRules() => [.. LoadRuleNodes().OfType<JsonObject>().Select(o => new PermRuleView(
        Str(o["agentPattern"]),
        EnumName(o["mode"], PermissionModeNames, "auto"),
        EnumName(o["level"], PermissionLevelNames, "read"),
        StringArray(o["allowedTools"]),
        StringArray(o["deniedTools"])))];


    private static Task<int> PermissionListRulesAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var rules = LoadRules();
        var sb = new StringBuilder();
        sb.AppendLine("ℹ 权限规则列表");
        sb.AppendLine($"共 {rules.Count} 条规则");
        sb.AppendLine($"文件: {RulesPathOrNull() ?? "(当前目录不在 git 工作区内)"}");
        sb.AppendLine();
        if (rules.Count == 0) sb.AppendLine("暂无权限规则");
        foreach (var r in rules) sb.AppendLine($"  Agent: {r.AgentPattern} Mode: {r.Mode} Level: {r.Level} Denied: {string.Join(",", r.Denied)} Allowed: {string.Join(",", r.Allowed)}");
        Console.Out.Write(sb.ToString());
        return Task.FromResult(0);
    }

    private static Task<int> PermissionAddRuleAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var pattern = Required(o, "agent_pattern", "permission_add_rule");
        var mode = Required(o, "mode", "permission_add_rule").Trim().ToLowerInvariant();
        var modeIndex = Array.IndexOf(PermissionModeNames, mode);
        if (modeIndex < 0) throw new ArgumentException($"mode 必须是 {string.Join("/", PermissionModeNames)} 之一，当前 '{mode}'。");
        var level = Get(o, "level", "read").Trim().ToLowerInvariant();
        var levelIndex = Array.IndexOf(PermissionLevelNames, level);
        if (levelIndex < 0) throw new ArgumentException($"level 必须是 {string.Join("/", PermissionLevelNames)} 之一，当前 '{level}'。");

        var rule = new JsonObject {
            ["agentPattern"] = pattern,
            ["mode"] = modeIndex,
            ["level"] = levelIndex
        };
        var allowed = SplitCsv(Nullable(o, "allowed_tools"));
        if (allowed.Length > 0) rule["allowedTools"] = new JsonArray([.. allowed.Select(t => JsonValue.Create(t))]);
        var denied = SplitCsv(Nullable(o, "denied_tools"));
        if (denied.Length > 0) rule["deniedTools"] = new JsonArray([.. denied.Select(t => JsonValue.Create(t))]);
        var description = Nullable(o, "description");
        if (!string.IsNullOrWhiteSpace(description)) rule["description"] = description;
        if (GetInt(o, "priority", 0) is var priority && priority != 0) rule["priority"] = priority;

        var rules = LoadRuleNodes();
        var replaced = false;
        for (var i = 0; i < rules.Count; i++) {
            if (rules[i] is JsonObject existing && Str(existing["agentPattern"]) == pattern) {
                rules[i] = rule;
                replaced = true;
                break;
            }
        }
        if (!replaced) rules.Add((JsonNode?)rule);
        SaveRuleNodes(rules);

        Console.Out.WriteLine($"{(replaced ? "权限规则已更新" : "权限规则已添加")}: {pattern} mode={mode} level={level}");
        Console.Out.WriteLine($"文件: {RulesPathOrNull()}");
        if (allowed.Length > 0) Console.Out.WriteLine($"  允许工具: {string.Join(", ", allowed)}");
        if (denied.Length > 0) Console.Out.WriteLine($"  拒绝工具: {string.Join(", ", denied)}");
        return Task.FromResult(0);
    }

    private static Task<int> PermissionRemoveRuleAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var pattern = Required(o, "agent_pattern", "permission_remove_rule");
        var rules = LoadRuleNodes();
        var removed = false;
        for (var i = rules.Count - 1; i >= 0; i--) {
            if (rules[i] is JsonObject existing && Str(existing["agentPattern"]) == pattern) {
                rules.RemoveAt(i);
                removed = true;
            }
        }
        if (!removed) {
            Console.Out.WriteLine($"未找到匹配 '{pattern}' 的权限规则（共 {rules.Count} 条）。先 permission_list_rules 看看现有模式。");
            return Task.FromResult(1);
        }
        SaveRuleNodes(rules);
        Console.Out.WriteLine($"权限规则已移除: {pattern}（剩余 {rules.Count} 条）");
        return Task.FromResult(0);
    }

    private static Task<int> PermissionClearRulesAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var confirm = Get(o, "confirm", "");
        if (confirm != "yes")
            throw new ArgumentException("permission_clear_rules 会清空全部权限规则，必须传 confirm=yes 确认。");
        var before = LoadRuleNodes().Count;
        SaveRuleNodes([]);
        Console.Out.WriteLine($"权限规则已清空（原有 {before} 条）");
        return Task.FromResult(0);
    }

    private static string[] SplitCsv(string? raw) => string.IsNullOrWhiteSpace(raw)
        ? []
        : [.. raw.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(t => t.Trim()).Where(t => t.Length > 0)];

    private static Task<int> PermissionCheckToolAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var tool = Required(o, "tool_name", "permission_check_tool");
        var agent = Get(o, "agent_id", "default");
        var rule = LoadRules().FirstOrDefault(r => r.AgentPattern == "*" || agent.Contains(r.AgentPattern.TrimStart('*'), StringComparison.OrdinalIgnoreCase));
        var denied = rule?.Denied.Contains(tool, StringComparer.OrdinalIgnoreCase) ?? false;
        var allowed = rule is null || (!denied && (rule.Allowed.Length == 0 || rule.Allowed.Contains(tool, StringComparer.OrdinalIgnoreCase)));
        Console.Out.WriteLine($"工具 '{tool}' (agent={agent}): {(allowed ? "ALLOWED" : "DENIED")}  规则: {rule?.AgentPattern ?? "(无匹配规则，默认允许)"} Level: {rule?.Level ?? "default"}");
        return Task.FromResult(0);
    }

    private static Task<int> PermissionCheckPathAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var path = Required(o, "path", "permission_check_path");
        var full = Path.GetFullPath(path);
        var deniedFragments = new[] { "..", "\\windows\\system32", "/etc/shadow" };
        var denied = deniedFragments.Any(f => full.Contains(f, StringComparison.OrdinalIgnoreCase));
        Console.Out.WriteLine($"路径 '{full}': {(denied ? "DENIED (敏感路径)" : "ALLOWED")}");
        return Task.FromResult(0);
    }

    private static Task<int> PermissionGetAgentRuleAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var agent = Required(o, "agent_name", "permission_get_agent_rule");
        var rule = LoadRules().FirstOrDefault(r => r.AgentPattern == "*" || agent.Contains(r.AgentPattern.TrimStart('*'), StringComparison.OrdinalIgnoreCase));
        Console.Out.WriteLine(rule is null
            ? $"Agent '{agent}' 无专属规则（使用默认规则）"
            : $"Agent: {rule.AgentPattern} Mode: {rule.Mode} Level: {rule.Level} Denied: {string.Join(",", rule.Denied)} Allowed: {string.Join(",", rule.Allowed)}");
        return Task.FromResult(0);
    }

    // ==================== model_search / policy_list ====================

    private static Task<int> ModelSearchAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var query = Get(o, "query", "");
        if (!File.Exists(Path.Combine(Jcc, "settings.json"))) { Console.Out.WriteLine("无模型配置"); return Task.FromResult(0); }
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Jcc, "settings.json")).TrimStart('\uFEFF'));
        var sb = new StringBuilder();
        sb.AppendLine("可用模型:");
        if (doc.RootElement.TryGetProperty("vendor", out var vendors) && vendors.ValueKind == JsonValueKind.Object) {
            foreach (var v in vendors.EnumerateObject()) {
                var model = v.Value.TryGetProperty("model", out var m) ? m.GetString() : "";
                var endpoint = v.Value.TryGetProperty("endpoint", out var e) ? e.GetString() : "";
                if (query.Length == 0 || v.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || (model ?? "").Contains(query, StringComparison.OrdinalIgnoreCase))
                    sb.AppendLine($"  {v.Name}: {model} ({endpoint})");
            }
        }
        Console.Out.Write(sb.ToString());
        return Task.FromResult(0);
    }

    private static Task<int> PolicyListAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var path = Path.Combine(Jcc, "policies.json");
        if (!File.Exists(path)) { Console.Out.WriteLine("暂无策略规则"); return Task.FromResult(0); }
        Console.Out.Write(File.ReadAllText(path));
        return Task.FromResult(0);
    }

    // ==================== 通知 / Win32 交互 / brief ====================

    private static Task<int> PushNotificationAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var title = Required(o, "title", "push_notification");
        var message = Get(o, "message", "");
        // Windows toast 走 PowerShell（BCL Process），失败则优雅降级为文本
        try {
            var ps = $"Add-Type -AssemblyName System.Windows.Forms; [System.Windows.Forms.MessageBox]::Show('{message.Replace("'", "''")}', '{title.Replace("'", "''")}')";
            var psi = new ProcessStartInfo("powershell.exe") { UseShellExecute = false, CreateNoWindow = true };
            psi.ArgumentList.Add("-NoProfile"); psi.ArgumentList.Add("-Command"); psi.ArgumentList.Add(ps);
            using var p = Process.Start(psi);
            Console.Out.WriteLine($"通知已发送: {title}");
        } catch (Exception ex) { Console.Out.WriteLine($"通知发送失败（已降级为文本）: {title} - {message} ({ex.Message})"); }
        return Task.FromResult(0);
    }

    private static Task<int> RemoteTriggerAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var url = Required(o, "url", "remote_trigger");
        var method = Get(o, "method", "POST").ToUpperInvariant();
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        using var req = new HttpRequestMessage(new HttpMethod(method), url);
        var token = Environment.GetEnvironmentVariable("JCC_REMOTE_TRIGGER_TOKEN");
        if (!string.IsNullOrEmpty(token)) req.Headers.TryAddWithoutValidation("Authorization", "Bearer " + token);
        try {
            using var resp = http.Send(req, ct);
            var body = resp.Content.ReadAsStringAsync(ct).GetAwaiter().GetResult();
            Console.Out.WriteLine($"触发成功 ({(int)resp.StatusCode}): {Truncate(body, 500)}");
        } catch (Exception ex) { Console.Error.WriteLine($"remote_trigger 失败: {ex.Message}"); return Task.FromResult(2); }
        return Task.FromResult(0);
    }

    [DllImport("user32.dll")] private static extern bool FlashWindowEx(ref FlashInfo pwfi);
    [DllImport("user32.dll")] private static extern bool MoveWindow(IntPtr hWnd, int x, int y, int w, int h, bool repaint);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out WinRect rect);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [StructLayout(LayoutKind.Sequential)] private struct FlashInfo { public int cbSize; public IntPtr hwnd; public uint dwFlags; public uint uCount; public uint dwTimeout; }
    [StructLayout(LayoutKind.Sequential)] private struct WinRect { public int L, T, R, B; }

    private static Task<int> ShakeWindowAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) { Console.Out.WriteLine("未找到前台窗口"); return Task.FromResult(0); }
        if (!GetWindowRect(hwnd, out var r)) { Console.Out.WriteLine("获取窗口矩形失败"); return Task.FromResult(0); }
        var x = r.L; var y = r.T; var w = r.R - r.L; var h = r.B - r.T;
        for (var i = 0; i < 6; i++) {
            MoveWindow(hwnd, x + (i % 2 == 0 ? 8 : -8), y, w, h, true);
            Thread.Sleep(40);
            MoveWindow(hwnd, x, y, w, h, true);
            Thread.Sleep(40);
        }
        Console.Out.WriteLine("窗口抖动完成");
        return Task.FromResult(0);
    }

    private static Task<int> FlashTaskbarAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var hwnd = GetForegroundWindow();
        if (hwnd == IntPtr.Zero) { Console.Out.WriteLine("未找到前台窗口"); return Task.FromResult(0); }
        var fi = new FlashInfo { cbSize = Marshal.SizeOf<FlashInfo>(), hwnd = hwnd, dwFlags = 3, uCount = 5, dwTimeout = 100 };
        FlashWindowEx(ref fi);
        Console.Out.WriteLine("任务栏闪烁完成");
        return Task.FromResult(0);
    }

    private static Task<int> SendUserMessageAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var msg = Required(o, "message", "send_user_message");
        Console.Out.WriteLine("Message delivered to user: " + msg);
        return Task.FromResult(0);
    }

    private static Task<int> BriefStatusAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var path = Path.Combine(Directory.GetCurrentDirectory(), ".jcc", "brief.json");
        if (!File.Exists(path)) { Console.Out.WriteLine("Brief mode: off"); return Task.FromResult(0); }
        try {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            var enabled = doc.RootElement.TryGetProperty("enabled", out var e) && e.GetBoolean();
            Console.Out.WriteLine($"Brief mode: {(enabled ? "on" : "off")}");
        } catch (JsonException) { Console.Out.WriteLine("Brief mode: unknown (invalid brief.json)"); }
        return Task.FromResult(0);
    }

    // ==================== 辅助 ====================

    private static async Task<(int exit, string stdout, string stderr)> RunProcCaptureAsync(string exe, string[] args, string workDir, CancellationToken ct) {
        try {
            var psi = new ProcessStartInfo(exe) { WorkingDirectory = workDir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
            foreach (var a in args) psi.ArgumentList.Add(a);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMinutes(2));
            var proc = Process.Start(psi);
            if (proc is null) return (-1, "", "无法启动 " + exe);
            using var p = proc;
            var so = p.StandardOutput.ReadToEndAsync(); var se = p.StandardError.ReadToEndAsync();
            await p.WaitForExitAsync(cts.Token).ConfigureAwait(false);
            return (p.ExitCode, await so.ConfigureAwait(false), await se.ConfigureAwait(false));
        } catch (Exception ex) { return (-1, "", ex.Message); }
    }
    private static bool Has(string s, params string[] keys) => keys.Any(k => s.Contains(k, StringComparison.OrdinalIgnoreCase));
    private static string Truncate(string s, int max) => s.Length <= max ? s : s[..max] + "...";
    private static string Get(IReadOnlyDictionary<string, string> o, string key, string fallback) => o.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v) ? v : fallback;
}
