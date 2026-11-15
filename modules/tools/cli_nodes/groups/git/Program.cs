using System.Reflection;

namespace JoinCode.CliNode.Git;

/// <summary>
/// git 分组独立 CLI.exe 入口 — ADR 0157 v2「按分组一个 exe」的第一个真实样板。
/// -h 文本由 migrate_mcp_tools_to_cli_nodes.py 从现有 [McpTool] 特性编译期提取，
/// 本程序不重复维护任何工具说明，也不引用引擎。
/// </summary>
internal static class Program {
    private const string HelpResource = "JoinCode.CliNodes.Groups.git.help.txt";

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
        var rest = args[1..];
        if (rest.Length > 0 && IsHelp(rest[0])) {
            PrintToolHelp(toolId);
            return 0;
        }
        if (!GitCommandCatalog.TryGet(toolId, out var build))
            return WriteUnknownTool(toolId);

        var bag = new ArgBag(rest);
        string gitArgs;
        try {
            gitArgs = build(bag);
        } catch (ArgumentException ex) {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
        var workDir = toolId == "git_clone" ? bag.Get("parent_dir") : bag.WorkingDirectory;
        return await GitRunner.RunAsync(gitArgs, workDir, ct).ConfigureAwait(false);
    }

    private static bool IsHelp(string token) => token is "-h" or "--help" or "help";

    private static void PrintHelp() => Console.Out.Write(LoadHelp());

    private static void PrintToolHelp(string toolId) {
        // Python 在 Windows 写出的文件是 CRLF，先归一再按空行切段。
        var text = LoadHelp().Replace("\r\n", "\n");
        var marker = $"  {toolId} — ";
        var start = text.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0) {
            PrintHelp();
            return;
        }
        var boundaries = new[] {
            text.IndexOf("\n\n  ", start + marker.Length, StringComparison.Ordinal),
            text.IndexOf("\n描述来源:", start, StringComparison.Ordinal),
            text.Length
        }.Where(index => index > start).ToArray();
        var end = boundaries.Length > 0 ? boundaries.Min() : text.Length;
        Console.Out.WriteLine($"jcc-tool-git.exe {toolId} — 参数说明（复用现有 [McpToolParameter] 特性）");
        Console.Out.WriteLine(text[start..end].TrimEnd());
    }

    private static int WriteUnknownTool(string toolId) {
        Console.Error.WriteLine($"未知工具 '{toolId}'（由 jcc-tool-git 的第一个参数触发，GitCommandCatalog 查表层拦截）。");
        Console.Error.WriteLine($"可用工具: {string.Join(", ", GitCommandCatalog.ToolIds)}");
        Console.Error.WriteLine("正确写法: jcc-tool-git.exe -h 查看全部分组说明，或 jcc-tool-git.exe <工具名> -h 查看该工具参数。");
        return 2;
    }

    private static string LoadHelp() {
        using var stream = typeof(Program).Assembly.GetManifestResourceStream(HelpResource)
            ?? throw new InvalidOperationException($"未找到内嵌资源 '{HelpResource}'，请检查 csproj 的 EmbeddedResource 声明");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
