namespace JoinCode.CliCommands;

/// <summary>
/// CLI 层工具节点发现入口 — ADR 0157 v2 第 4 点。
/// 目录树 + 关键词搜索下钻，取代 MCP 的 tool_groups / 三级 map[] 语法。
/// 结果只打印到 stdout，LLM 用 bash 按需读取，查完即弃，永不注入系统提示词。
/// </summary>
internal static class CliToolCommand {
    private const string ToolsDirEnvVar = "JCC_CLI_TOOLS_DIR";

    /// <summary>执行 jcc tool 元命令的动作分发。</summary>
    /// <param name="args">完整命令行参数，args[0] 为 tool，args[1] 为动作。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>进程退出码。</returns>
    public static Task<int> ExecuteAsync(string[] args, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        var action = args.Length > 1 ? args[1] : "tree";
        return Task.FromResult(action switch {
            "tree" => RunTree(args),
            "find" => RunFind(args),
            "base" => RunBase(),
            "help" => RunHelp(args),
            "-h" or "--help" => WriteUsage(),
            _ => WriteUnknownAction(action)
        });
    }

    private static int RunTree(string[] args) {
        var dir = ResolveToolsDir(args);
        var nodes = NodeManifestLoader.LoadNodesFromDirectory(dir);
        Console.Out.WriteLine($"CLI 工具节点目录: {dir}");
        Console.Out.WriteLine(nodes.Count == 0
            ? "  (未发现 node.yaml — 用 --dir 指定工具目录，或设置 JCC_CLI_TOOLS_DIR)"
            : CliToolSearch.RenderTree(nodes));
        return 0;
    }

    private static int RunFind(string[] args) {
        var query = ExtractQuery(args);
        if (string.IsNullOrWhiteSpace(query)) {
            Console.Error.WriteLine("缺少查询关键词（由 jcc tool find 的参数触发，CliToolCommand 参数校验层拦截）。");
            Console.Error.WriteLine("正确写法: jcc tool find pr check --dir <工具目录>");
            return 2;
        }
        var dir = ResolveToolsDir(args);
        var matches = CliToolSearch.Find(NodeManifestLoader.LoadNodesFromDirectory(dir), query);
        Console.Out.WriteLine($"查询: {query}  |  目录: {dir}");
        Console.Out.WriteLine(CliToolSearch.RenderResults(matches));
        return 0;
    }

    private static int RunBase() {
        var registry = BaseToolRegistry.LoadDefault();
        Console.Out.WriteLine($"基础工具（来自 base.yaml，共 {registry.Count} 个 — 首轮 tools 字段唯一来源）:");
        foreach (var tool in registry.All)
            Console.Out.WriteLine($"  {tool.Id,-18} {tool.Name,-16} {tool.Summary}");
        return 0;
    }

    private static int RunHelp(string[] args) {
        var id = args.Length > 2 ? args[2] : null;
        if (string.IsNullOrWhiteSpace(id)) return WriteUsage();
        var node = NodeManifestLoader.LoadNodesFromDirectory(ResolveToolsDir(args))
            .FirstOrDefault(candidate => string.Equals(candidate.Id, id, StringComparison.OrdinalIgnoreCase));
        if (node is null) {
            Console.Error.WriteLine($"未找到节点 '{id}'（由 jcc tool help 的参数触发，CliToolCommand 节点查找层拦截）。");
            Console.Error.WriteLine("正确写法: 先用 jcc tool find <关键词> 找到节点 ID，再 jcc tool help <ID>。");
            return 1;
        }
        Console.Out.WriteLine(CliToolHelp.Render(node));
        return 0;
    }

    private static int WriteUsage() {
        Console.Out.WriteLine("jcc tool — CLI 工具节点发现（ADR 0157 v2）");
        Console.Out.WriteLine();
        Console.Out.WriteLine("用法:");
        Console.Out.WriteLine("  jcc tool tree [--dir <path>]              列出目录树");
        Console.Out.WriteLine("  jcc tool find <关键词...> [--dir <path>]  按关键词搜索下钻");
        Console.Out.WriteLine("  jcc tool help <节点ID> [--dir <path>]     阅读某节点的完整参数");
        Console.Out.WriteLine("  jcc tool base                             列出 4 个基础工具");
        Console.Out.WriteLine();
        Console.Out.WriteLine($"工具目录优先级: --dir > 环境变量 {ToolsDirEnvVar} > <exe目录>/tools");
        Console.Out.WriteLine("每个独立工具也可用 <exe> -h 直接阅读自身说明。");
        return 0;
    }

    private static int WriteUnknownAction(string action) {
        Console.Error.WriteLine($"未知动作 '{action}'（由 jcc tool 的第二个参数触发，CliToolCommand 动作分发层拦截）。");
        Console.Error.WriteLine("可用动作: tree | find | base | help");
        Console.Error.WriteLine("正确写法: jcc tool find <关键词> --dir <工具目录>");
        return 2;
    }

    private static string ResolveToolsDir(string[] args) {
        var explicitDir = FlatSubCommandRouter.GetOptionValue(args, "--dir");
        if (!string.IsNullOrWhiteSpace(explicitDir)) return explicitDir;
        var envDir = Environment.GetEnvironmentVariable(ToolsDirEnvVar);
        if (!string.IsNullOrWhiteSpace(envDir)) return envDir;
        return Path.Combine(AppContext.BaseDirectory, "tools");
    }

    private static string ExtractQuery(string[] args) {
        var tokens = new List<string>();
        for (var i = 2; i < args.Length; i++) {
            if (args[i] == "--dir") {
                i++;
                continue;
            }
            if (args[i].StartsWith("--")) continue;
            tokens.Add(args[i]);
        }
        return string.Join(" ", tokens);
    }
}
