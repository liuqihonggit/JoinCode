// system 组下沉 handler — config/错误诊断/permission 读/model_search/policy_list/通知交互/brief。
// 全部只用 BCL + Win32 + HttpClient。注意分界：
// - 写类（config_set/permission 写/brief_mode/memory 写）需宿主监听文件重载才生效，本批不动，避免"改了宿主不认"。
// - agent_*/analytics_*/TUI(ask_user/confirm)/watcher(memory_team_sync) 绑定宿主进程内状态，物理边界，留宿主。
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using JoinCode.Abstractions.Tools;

namespace JoinCode.CliNode.System;

internal static partial class SystemCommands {
    private static string Home => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    private static string Jcc => Path.Combine(Home, ".jcc");

    // ==================== config（读） ====================

    private static Task<int> ConfigGetAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var key = Required(o, "setting", "config_get");
        // 支持的 key（与 SupportedSettings 对齐的常用集）
        string[] supported = ["theme", "model", "debuglog", "thinkingEnabled", "telemetry", "autoFetchModels", "language", "maxTokens", "temperature"];
        if (!supported.Contains(key, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"config_get 不支持设置项 '{key}'。可用: {string.Join(", ", supported)}");
        var value = ReadSetting(key, out var found);
        Console.Out.WriteLine($"{key} = {(found ? value : "(未设置)")}");
        return Task.FromResult(0);
    }

    private static Task<int> ConfigListAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        string[] supported = ["theme", "model", "debuglog", "thinkingEnabled", "telemetry", "autoFetchModels", "language", "maxTokens", "temperature"];
        var sb = new StringBuilder();
        sb.AppendLine("Available settings:");
        sb.AppendLine();
        sb.AppendLine("Global settings:");
        foreach (var k in supported) {
            var v = ReadSetting(k, out var found);
            sb.AppendLine($"  - {k}: {(found ? v : "(not set)")}");
        }
        Console.Out.Write(sb.ToString());
        return Task.FromResult(0);
    }

    private static string ReadSetting(string key, out bool found) {
        found = false;
        foreach (var file in new[] { Path.Combine(Jcc, "settings.json"), Path.Combine(Jcc, "global.json") }) {
            if (!File.Exists(file)) continue;
            try {
                using var doc = JsonDocument.Parse(File.ReadAllText(file).TrimStart('\uFEFF'));
                if (FindKey(doc.RootElement, key, out var v)) { found = true; return v; }
            } catch (JsonException) { /* 跳过损坏文件 */ }
        }
        return "";
    }
    private static bool FindKey(JsonElement el, string key, out string value) {
        value = "";
        if (el.ValueKind != JsonValueKind.Object) return false;
        foreach (var p in el.EnumerateObject()) {
            if (p.Name.Equals(key, StringComparison.OrdinalIgnoreCase)) {
                value = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() ?? "" : p.Value.ToString();
                return true;
            }
            if (p.Value.ValueKind == JsonValueKind.Object && FindKey(p.Value, key, out value)) return true;
        }
        return false;
    }

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

    // ==================== permission（读） ====================

    private static string RulesPath {
        get {
            var repoRules = Path.Combine(Directory.GetCurrentDirectory(), ".jcc", "permission", "rules.json");
            if (File.Exists(repoRules)) return repoRules;
            return Path.Combine(Jcc, "permission", "rules.json");
        }
    }
    private sealed class PermRule {
        public string? AgentPattern { get; set; }
        public string? Level { get; set; }
        public List<string>? DeniedTools { get; set; }
        public List<string>? AllowedTools { get; set; }
    }
    private static List<PermRule> LoadRules() {
        if (!File.Exists(RulesPath)) return [];
        try { return JsonSerializer.Deserialize(File.ReadAllText(RulesPath), SystemJsonContext.Default.ListPermRule) ?? []; }
        catch (JsonException) { return []; }
    }

    private static Task<int> PermissionListRulesAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var rules = LoadRules();
        var sb = new StringBuilder();
        sb.AppendLine("ℹ 权限规则列表");
        sb.AppendLine($"共 {rules.Count} 条规则");
        sb.AppendLine();
        if (rules.Count == 0) sb.AppendLine("暂无权限规则");
        foreach (var r in rules) sb.AppendLine($"  Agent: {r.AgentPattern} Level: {r.Level} Denied: {string.Join(",", r.DeniedTools ?? [])} Allowed: {string.Join(",", r.AllowedTools ?? [])}");
        Console.Out.Write(sb.ToString());
        return Task.FromResult(0);
    }

    private static Task<int> PermissionCheckToolAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var tool = Required(o, "tool_name", "permission_check_tool");
        var agent = Get(o, "agent_id", "default");
        var rules = LoadRules();
        var rule = rules.FirstOrDefault(r => r.AgentPattern is not null && (r.AgentPattern == "*" || agent.Contains(r.AgentPattern.TrimStart('*'), StringComparison.OrdinalIgnoreCase)));
        var allowed = rule is null || (rule.AllowedTools?.Contains(tool, StringComparer.OrdinalIgnoreCase) ?? false) || !(rule.DeniedTools?.Contains(tool, StringComparer.OrdinalIgnoreCase) ?? false);
        var denied = rule?.DeniedTools?.Contains(tool, StringComparer.OrdinalIgnoreCase) ?? false;
        Console.Out.WriteLine($"工具 '{tool}' (agent={agent}): {(denied ? "DENIED" : allowed ? "ALLOWED" : "DENIED")}  规则级别: {rule?.Level ?? "default"}");
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
        var rule = LoadRules().FirstOrDefault(r => r.AgentPattern is not null && (r.AgentPattern == "*" || agent.Contains(r.AgentPattern.TrimStart('*'), StringComparison.OrdinalIgnoreCase)));
        Console.Out.WriteLine(rule is null ? $"Agent '{agent}' 无专属规则（使用默认规则）" : $"Agent: {rule.AgentPattern} Level: {rule.Level} Denied: {string.Join(",", rule.DeniedTools ?? [])}");
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

    private sealed class PermRuleList : List<PermRule> { }
    [global::System.Text.Json.Serialization.JsonSerializable(typeof(List<PermRule>))]
    [global::System.Text.Json.Serialization.JsonSourceGenerationOptions(PropertyNameCaseInsensitive = true)]
    private sealed partial class SystemJsonContext : global::System.Text.Json.Serialization.JsonSerializerContext;
}
