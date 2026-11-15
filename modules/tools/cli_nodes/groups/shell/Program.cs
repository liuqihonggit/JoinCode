// HANDCRAFTED — DO NOT OVERWRITE BY GENERATOR
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JoinCode.CliNode.Shell;

/// <summary>
/// shell 分组独立 CLI.exe — 第 4 个「逐组做实」样板。
/// 6 个命令执行类工具用 BCL 本地执行；依赖引擎运行态的 21 个工具转发宿主 jcc mcp_call。
/// -h 文本来自现有 [McpTool] 特性，本文件不重复维护任何说明。
/// 手工实现文件：脚本检测到 HANDCRAFTED 标记会跳过覆盖。
/// </summary>
internal static class Program {
    private const string HelpResource = "JoinCode.CliNodes.Groups.shell.help.txt";

    private static async Task<int> Main(string[] args) {
        using var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
        return await RunAsync(args, cts.Token).ConfigureAwait(false);
    }

    private static async Task<int> RunAsync(string[] args, CancellationToken ct) {
        if (args.Length == 0 || IsHelp(args[0])) {
            PrintHelp();
            return 0;
        }
        var toolId = args[0];
        if (args.Length > 1 && IsHelp(args[1])) {
            PrintToolHelp(toolId);
            return 0;
        }
        var options = BuildArgs(args[1..]);
        var handler = ShellCommands.Find(toolId);
        if (handler is not null) {
            try {
                return await handler(options, ct).ConfigureAwait(false);
            } catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or InvalidOperationException) {
                Console.Error.WriteLine($"工具 '{toolId}' 本地执行失败（ShellCommands 本地执行层拦截）：{ex.Message}");
                Console.Error.WriteLine($"修复方式：{toolId} -h 查看必填参数与用法，或检查命令/路径/权限。");
                return 2;
            }
        }
        return await ForwardAsync(toolId, JsonSerializer.Serialize(options, GroupArgsJsonContext.Default.DictionaryStringString), ct)
            .ConfigureAwait(false);
    }

    private static Dictionary<string, string> BuildArgs(string[] args) {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++) {
            var token = args[i];
            if (token.StartsWith("--", StringComparison.Ordinal)) {
                var key = token[2..];
                var eq = key.IndexOf('=');
                if (eq > 0) {
                    result[key[..eq]] = key[(eq + 1)..];
                } else if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) {
                    result[key] = args[++i];
                } else {
                    result[key] = "true";
                }
            } else {
                result["arg" + (i + 1)] = token;
            }
        }
        return result;
    }

    private static async Task<int> ForwardAsync(string toolId, string payload, CancellationToken ct) {
        var host = Environment.GetEnvironmentVariable("JCC_HOST_EXE") ?? "jcc";
        var psi = new ProcessStartInfo(host) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.ArgumentList.Add("mcp_call");
        psi.ArgumentList.Add(toolId);
        psi.ArgumentList.Add(payload);
        try {
            using var process = Process.Start(psi) ?? throw new InvalidOperationException("无法启动宿主进程");
            var stdout = process.StandardOutput.ReadToEndAsync(ct);
            var stderr = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            Console.Out.Write(await stdout.ConfigureAwait(false));
            Console.Error.Write(await stderr.ConfigureAwait(false));
            return process.ExitCode;
        } catch (Exception ex) when (ex is global::System.ComponentModel.Win32Exception or InvalidOperationException) {
            Console.Error.WriteLine($"工具 '{toolId}' 依赖引擎运行态、无本地实现，需宿主 jcc 才能执行（转发层拦截）：{ex.Message}");
            Console.Error.WriteLine("修复方式：设置 JCC_HOST_EXE 指向 jcc.exe；或用 -h 只查看说明。");
            return 3;
        }
    }

    private static bool IsHelp(string token) => token is "-h" or "--help" or "help";

    private static void PrintHelp() => Console.Out.Write(LoadHelp().Replace("\r\n", "\n"));

    private static void PrintToolHelp(string toolId) {
        var text = LoadHelp().Replace("\r\n", "\n");
        var marker = $"  {toolId} — ";
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) { PrintHelp(); return; }
        var boundaries = new[] {
            text.IndexOf("\n\n  ", start + marker.Length, StringComparison.Ordinal),
            text.IndexOf("\n描述来源:", start, StringComparison.Ordinal),
            text.Length
        }.Where(index => index > start).ToArray();
        var end = boundaries.Length > 0 ? boundaries.Min() : text.Length;
        Console.Out.WriteLine($"shell 分组 / {toolId} — 参数说明（复用现有 [McpToolParameter] 特性）");
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
internal partial class GroupArgsJsonContext : JsonSerializerContext { }
