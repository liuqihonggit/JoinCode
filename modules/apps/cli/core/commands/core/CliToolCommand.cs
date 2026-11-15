namespace JoinCode.CliCommands;

/// <summary>
/// CLI 层工具节点发现 + 进程隔离执行入口 — ADR 0157 v3 第 4 点。
/// 目录树 + 关键词搜索下钻，取代 MCP 的 tool_groups / 三级 map[] 语法。
/// 结果只打印到 stdout，LLM 用 bash 按需读取，查完即弃，永不注入系统提示词。
/// <para>call 动作是「进程隔离」的执行面：jcc 主进程不加载任何工具实现 / 引擎 DI，
/// 只通过 Process.Start 启动对应分组 exe（jcc-tool-&lt;组&gt;.exe），透传参数并回传 stdout/stderr/退出码。
/// 工具实现运行在独立进程里，与宿主进程隔离 — 这正是 ADR 0157 把工具拆成 exe 的目的。</para>
/// </summary>
internal static class CliToolCommand {
    private const string ToolsDirEnvVar = "JCC_CLI_TOOLS_DIR";
    private const string ExeDirEnvVar = "JCC_TOOL_EXE_DIR";

    /// <summary>执行 jcc tool 元命令的动作分发。</summary>
    /// <param name="args">完整命令行参数，args[0] 为 tool，args[1] 为动作。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>进程退出码。</returns>
    public static async Task<int> ExecuteAsync(string[] args, CancellationToken ct) {
        ct.ThrowIfCancellationRequested();
        var action = args.Length > 1 ? args[1] : "tree";
        switch (action) {
            case "tree": return RunTree(args);
            case "find": return RunFind(args);
            case "base": return RunBase();
            case "help": return RunHelp(args);
            case "call": return await RunCallAsync(args, ct).ConfigureAwait(false);
            case "-h":
            case "--help": return WriteUsage();
            default: return WriteUnknownAction(action);
        }
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

    /// <summary>
    /// 进程隔离执行 — jcc 启动分组 exe 运行工具（ADR 0157 第 4 点）。
    /// <para>本方法只做三件事：定位 exe、启动进程、回传 stdout/stderr/退出码。
    /// 不初始化引擎 DI、不加载任何工具实现，工具逻辑全在被启动的独立进程里运行。</para>
    /// <para>用法: jcc tool call &lt;分组&gt; &lt;工具名&gt; [--参数 值 ...] [--exe &lt;path&gt;]</para>
    /// </summary>
    private static async Task<int> RunCallAsync(string[] args, CancellationToken ct) {
        var group = args.Length > 2 ? args[2] : null;
        var tool = args.Length > 3 ? args[3] : null;
        if (string.IsNullOrWhiteSpace(group) || string.IsNullOrWhiteSpace(tool)) {
            Console.Error.WriteLine("缺少分组或工具名（由 jcc tool call 的参数触发，CliToolCommand 参数校验层拦截）。");
            Console.Error.WriteLine("正确写法: jcc tool call <分组> <工具名> [--参数 值 ...]");
            Console.Error.WriteLine("示例: jcc tool call file file_read --file_path D:/tmp/a.txt");
            Console.Error.WriteLine("可用分组见: jcc tool tree");
            return 2;
        }

        // --exe 是本执行器自用的显式路径选项，不透传给工具 exe；其余参数原样透传。
        string? explicitExe = null;
        var passThrough = new List<string>();
        for (var i = 4; i < args.Length; i++) {
            if (args[i] == "--exe" && i + 1 < args.Length) {
                explicitExe = args[++i];
                continue;
            }
            passThrough.Add(args[i]);
        }

        var exe = !string.IsNullOrWhiteSpace(explicitExe) ? explicitExe : ResolveGroupExe(group);
        if (string.IsNullOrWhiteSpace(exe) || !File.Exists(exe)) {
            Console.Error.WriteLine($"未找到分组 '{group}' 的独立 exe（由 jcc tool call 的 exe 定位层拦截）：{exe ?? "(未解析出路径)"}");
            Console.Error.WriteLine("原因: 该分组 exe 尚未编译，或不在默认查找路径。");
            Console.Error.WriteLine("正确写法: 先 dotnet build 该分组 csproj，或用 --exe <path> 显式指定 exe 路径。");
            Console.Error.WriteLine($"查找顺序: --exe > {ExeDirEnvVar} > {ToolsDirEnvVar}/<组> > <jcc目录>/tools/<组> > 仓库 artifacts/bin");
            return 3;
        }

        var psi = new ProcessStartInfo(exe) {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        // ArgumentList 逐参数传递，不手工拼命令行字符串 — 避免空格/引号/特殊字符转义问题。
        psi.ArgumentList.Add(tool);
        foreach (var arg in passThrough)
            psi.ArgumentList.Add(arg);

        try {
            using var process = Process.Start(psi)
                ?? throw new InvalidOperationException("系统未返回新进程");
            var stdoutTask = process.StandardOutput.ReadToEndAsync(ct);
            var stderrTask = process.StandardError.ReadToEndAsync(ct);
            await process.WaitForExitAsync(ct).ConfigureAwait(false);
            var stdout = await stdoutTask.ConfigureAwait(false);
            var stderr = await stderrTask.ConfigureAwait(false);
            if (!string.IsNullOrEmpty(stdout)) Console.Out.Write(stdout);
            if (!string.IsNullOrEmpty(stderr)) Console.Error.Write(stderr);
            return process.ExitCode;
        } catch (Exception ex) when (ex is global::System.ComponentModel.Win32Exception
                                       or InvalidOperationException
                                       or OperationCanceledException) {
            Console.Error.WriteLine($"分组 exe 启动/执行失败（进程隔离层拦截）：{ex.Message}");
            Console.Error.WriteLine($"exe: {exe}");
            Console.Error.WriteLine("正确写法: 确认 exe 已编译且可执行，或用 --exe <path> 显式指定。");
            return 4;
        }
    }

    /// <summary>按优先级定位分组 exe：显式目录 > 平铺目录 > 工具目录树 > 发布布局 > 仓库开发布局。</summary>
    private static string? ResolveGroupExe(string group) {
        var exeName = $"jcc-tool-{group}.exe";

        var flatDir = Environment.GetEnvironmentVariable(ExeDirEnvVar);
        if (!string.IsNullOrWhiteSpace(flatDir)) {
            var candidate = Path.Combine(flatDir, exeName);
            if (File.Exists(candidate)) return candidate;
        }

        var toolsDir = Environment.GetEnvironmentVariable(ToolsDirEnvVar);
        if (!string.IsNullOrWhiteSpace(toolsDir)) {
            var candidate = Path.Combine(toolsDir, group, exeName);
            if (File.Exists(candidate)) return candidate;
        }

        var published = Path.Combine(AppContext.BaseDirectory, "tools", group, exeName);
        if (File.Exists(published)) return published;

        var repoRoot = FindRepoRoot(AppContext.BaseDirectory);
        if (repoRoot is not null) {
            foreach (var cfg in new[] { "Debug", "Release" }) {
                var groupBin = Path.Combine(repoRoot, "artifacts", "bin", $"jcc_tool_{group}", cfg);
                if (!Directory.Exists(groupBin)) continue;
                foreach (var tfmDir in Directory.EnumerateDirectories(groupBin)) {
                    var candidate = Path.Combine(tfmDir, exeName);
                    if (File.Exists(candidate)) return candidate;
                }
            }
        }
        return null;
    }

    /// <summary>从 exe 所在目录向上找含 artifacts/bin 的仓库根目录。</summary>
    private static string? FindRepoRoot(string start) {
        var dir = new DirectoryInfo(start);
        for (var i = 0; i < 8 && dir is not null; i++, dir = dir.Parent) {
            if (Directory.Exists(Path.Combine(dir.FullName, "artifacts", "bin")))
                return dir.FullName;
        }
        return null;
    }

    private static int WriteUsage() {
        Console.Out.WriteLine("jcc tool — CLI 工具节点发现 + 进程隔离执行（ADR 0157 v3）");
        Console.Out.WriteLine();
        Console.Out.WriteLine("用法:");
        Console.Out.WriteLine("  jcc tool tree [--dir <path>]              列出目录树");
        Console.Out.WriteLine("  jcc tool find <关键词...> [--dir <path>]  按关键词搜索下钻");
        Console.Out.WriteLine("  jcc tool help <节点ID> [--dir <path>]     阅读某节点的完整参数");
        Console.Out.WriteLine("  jcc tool call <分组> <工具名> [--参数 值]  进程隔离执行：启动分组 exe 运行工具");
        Console.Out.WriteLine("                                    [--exe <path>] 显式指定 exe 路径");
        Console.Out.WriteLine("  jcc tool base                             列出 4 个基础工具");
        Console.Out.WriteLine();
        Console.Out.WriteLine($"工具目录优先级: --dir > 环境变量 {ToolsDirEnvVar} > <exe目录>/tools");
        Console.Out.WriteLine($"exe 定位优先级: --exe > {ExeDirEnvVar} > {ToolsDirEnvVar}/<组> > <exe目录>/tools/<组> > 仓库 artifacts/bin");
        Console.Out.WriteLine("每个独立工具也可用 <exe> -h 直接阅读自身说明。");
        return 0;
    }

    private static int WriteUnknownAction(string action) {
        Console.Error.WriteLine($"未知动作 '{action}'（由 jcc tool 的第二个参数触发，CliToolCommand 动作分发层拦截）。");
        Console.Error.WriteLine("可用动作: tree | find | base | help | call");
        Console.Error.WriteLine("正确写法: jcc tool call <分组> <工具名> [--参数 值]");
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
