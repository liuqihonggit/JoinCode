// HANDCRAFTED — DO NOT OVERWRITE BY GENERATOR
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using JoinCode.Abstractions.Tools;

namespace JoinCode.CliNode.Slash;

/// <summary>
/// 斜杠命令独立 CLI.exe — ADR 0157「mcp 和斜杠命令都拆成 exe」。
/// -h 文本来自现有 [ChatCommand] / [ChatCommandArg] 特性（编译期内嵌），本文件不重复维护任何说明。
/// 斜杠命令依赖会话状态（聊天上下文 / 服务容器），无法零依赖实现，执行统一转发宿主 jcc slash_call；
/// jcc 侧通过 `jcc tool call slash &lt;命令&gt;` 启动本 exe，形成进程隔离调用。
/// </summary>
internal static class Program {
    private const string HelpResource = "JoinCode.CliNodes.Groups.slash.help.txt";

    private static async Task<int> Main(string[] args) {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        return await RunAsync(RestoreMsysPathExpansion(args), cts.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// 恢复 MSYS2/Git Bash 的路径展开 — 委托契约层 <see cref="CliArgNormalizer"/>（唯一数据源）。
    /// Git Bash 会把以 / 开头的命令名（如 /agents）当成 Unix 路径展开为 &lt;Git安装根&gt;/agents，
    /// 不恢复的话斜杠命令名全部失配。此处不再保留私有副本，避免与 jcc 主程序行为分叉。
    /// </summary>
    private static string[] RestoreMsysPathExpansion(string[] args) => CliArgNormalizer.RestoreMsysPathExpansion(args);

    private static async Task<int> RunAsync(string[] args, CancellationToken ct) {
        if (args.Length == 0 || IsHelp(args[0])) {
            PrintHelp();
            return 0;
        }
        if (CliNodeCoverage.IsRequested(args[0])) {
            Console.Out.Write(CliNodeCoverage.Render("slash", []));
            return 0;
        }
        var command = NormalizeCommandName(args[0]);
        if (args.Length > 1 && IsHelp(args[1])) {
            PrintCommandHelp(command);
            return 0;
        }
        var argsJson = BuildArgsJson(args[1..]);
        return await ForwardAsync(command, argsJson, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// 归一化命令名 — 兼容 /agents、agents、以及 Git Bash 把 /agents 展开成的 Windows 路径。
    /// 委托契约层 <see cref="CliArgNormalizer.NormalizeNodeName"/>，与 jcc 执行器同一规则。
    /// </summary>
    private static string NormalizeCommandName(string raw) => CliArgNormalizer.NormalizeNodeName(raw);

    /// <summary>把 --key 值 / --key=值 / 位置参数组装成 JSON 对象；首参已是 JSON 时原样透传。</summary>
    private static string BuildArgsJson(string[] args) {
        if (args.Length == 1 && args[0].TrimStart().StartsWith("{")) return args[0];
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < args.Length; i++) {
            var token = args[i];
            if (token.StartsWith("--", StringComparison.Ordinal)) {
                var key = token[2..];
                var eq = key.IndexOf('=');
                if (eq > 0) {
                    map[key[..eq]] = key[(eq + 1)..];
                } else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) {
                    map[key] = args[++i];
                } else {
                    map[key] = "true";
                }
            } else {
                map["arg" + (i + 1)] = token;
            }
        }
        return JsonSerializer.Serialize(map, SlashArgsJsonContext.Default.DictionaryStringString);
    }

    private static async Task<int> ForwardAsync(string command, string argsJson, CancellationToken ct) {
        var host = Environment.GetEnvironmentVariable("JCC_HOST_EXE") ?? "jcc";
        var psi = new ProcessStartInfo(host) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.ArgumentList.Add("slash_call");
        psi.ArgumentList.Add(command);
        psi.ArgumentList.Add(argsJson);
        try {
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("无法启动宿主进程");
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            Console.Out.Write(await stdout.ConfigureAwait(false));
            Console.Error.Write(await stderr.ConfigureAwait(false));
            return process.ExitCode;
        } catch (Exception ex) when (ex is global::System.ComponentModel.Win32Exception or InvalidOperationException) {
            Console.Error.WriteLine($"斜杠命令 '{command}' 需要宿主 jcc 才能执行（转发层拦截）：{ex.Message}");
            Console.Error.WriteLine("修复方式：设置 JCC_HOST_EXE 指向 jcc.exe，或把 jcc 加入 PATH；也可用 -h 只查看说明。");
            return 3;
        }
    }

    private static bool IsHelp(string token) => token is "-h" or "--help" or "help";

    private static void PrintHelp() => Console.Out.Write(LoadHelp().Replace("\r\n", "\n"));

    private static void PrintCommandHelp(string command) {
        var text = LoadHelp().Replace("\r\n", "\n");
        var marker = $"  /{command} — ";
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) { PrintHelp(); return; }
        var boundaries = new[] {
            text.IndexOf("\n\n  /", start + marker.Length, StringComparison.Ordinal),
            text.IndexOf("\n描述来源:", start, StringComparison.Ordinal),
            text.Length
        }.Where(index => index > start).ToArray();
        var end = boundaries.Length > 0 ? boundaries.Min() : text.Length;
        Console.Out.WriteLine($"slash 节点 / /{command} — 参数说明（复用现有 [ChatCommandArg] 特性）");
        Console.Out.WriteLine(text[start..end].TrimEnd());
    }

    private static string LoadHelp() {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream(HelpResource)
            ?? throw new InvalidOperationException($"未找到内嵌资源 '{HelpResource}'");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}

[JsonSerializable(typeof(Dictionary<string, string>))]
internal partial class SlashArgsJsonContext : JsonSerializerContext { }
