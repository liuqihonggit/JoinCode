// HANDCRAFTED — DO NOT OVERWRITE BY GENERATOR
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace JoinCode.CliNode.Tools;

/// <summary>
/// tools 分组独立 CLI.exe — 第 3 个「逐组做实」混合模式样板（前两个是 git / file 组）。
/// 本地命令表命中的工具用 BCL 本地执行；未本地化的工具自动转发宿主 jcc mcp_call。
/// -h 文本来自内嵌资源 JoinCode.CliNodes.Groups.tools.help.txt，本文件不重复维护任何工具说明。
/// 手工实现文件：脚本检测到无 GENERATED 标记会跳过覆盖。
/// </summary>
internal static class Program {
    private const string HelpResource = "JoinCode.CliNodes.Groups.tools.help.txt";

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
        var handler = ToolsCommands.Find(toolId);
        if (handler is not null) {
            try {
                return await handler(options, ct).ConfigureAwait(false);
            } catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException) {
                Console.Error.WriteLine(ex.Message);
                Console.Error.WriteLine($"修复提示: jcc-tool-tools.exe {toolId} -h 查看 [McpToolParameter] 参数说明与正确写法。");
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
            Console.Error.WriteLine($"error: 工具 `{toolId}` 无本地实现，需转发宿主 jcc 执行，但宿主进程启动失败");
            Console.Error.WriteLine($"  --> 触发参数: 第一个位置参数 `{toolId}`（ToolsCommands 本地命令表未命中 → 转入转发兜底）");
            Console.Error.WriteLine("  |");
            Console.Error.WriteLine($"  = 为什么失败: 宿主 jcc 不可启动（JCC_HOST_EXE 未设置或指向不存在的文件，且 PATH 中找不到 jcc），无法建立宿主子进程（{ex.Message}）");
            Console.Error.WriteLine("  = 哪层拦截: ForwardAsync 转发兜底层（jcc-tool-tools.exe，本地优先未命中后的回退路径）");
            Console.Error.WriteLine("  = 正确写法: 设置 JCC_HOST_EXE 指向 jcc.exe 全路径，或把 jcc 加入 PATH；仅需查看说明时用 -h");
            Console.Error.WriteLine("  = 退出码: 3");
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
        Console.Out.WriteLine($"tools 分组 / {toolId} — 参数说明（复用现有 [McpToolParameter] 特性）");
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
