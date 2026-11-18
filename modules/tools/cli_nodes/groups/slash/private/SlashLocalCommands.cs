// slash 组本地实现 — 5 个纯文本/环境命令（version/color/env/help/heapdump）。
// 其余 94 个依赖宿主会话上下文/TUI 回调/LLM 会话流/进程内长连接，转发 jcc slash_call。
// caveat：env 读到的是独立 exe 自身环境（无宿主会话级变量）；heapdump 诊断的是独立 exe 自身进程——如实标注。






namespace JoinCode.CliNode.Slash;

internal static class SlashLocalCommands {
    private static readonly FrozenDictionary<string, CliNodeHandler> Handlers =
        new Dictionary<string, CliNodeHandler>(StringComparer.Ordinal) {
            ["/version"] = VersionAsync,
            ["/color"] = ColorAsync,
            ["/env"] = EnvAsync,
            ["/help"] = HelpAsync,
            ["/heapdump"] = HeapdumpAsync
        }.ToFrozenDictionary(StringComparer.Ordinal);

    public static CliNodeHandler? Find(string id) => Handlers.TryGetValue(id, out var h) ? h : null;
    public static IEnumerable<string> LocalToolIds => Handlers.Keys.OrderBy(x => x, StringComparer.Ordinal);

    private static Task<int> VersionAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var asm = typeof(SlashLocalCommands).Assembly.GetName().Version?.ToString() ?? "1.0.0";
        Console.Out.WriteLine("JoinCode");
        Console.Out.WriteLine($"  版本: {asm}");
        Console.Out.WriteLine($"  运行时: {Environment.Version} (.NET {Environment.Version})");
        Console.Out.WriteLine($"  架构: {RuntimeInformation.ProcessArchitecture}");
        Console.Out.WriteLine($"  OS: {RuntimeInformation.OSDescription}");
        return Task.FromResult(0);
    }

    private static Task<int> ColorAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var action = (o.TryGetValue("action", out var a) ? a : "").Trim().ToLowerInvariant();
        if (action.Length == 0 || action == "test") {
            Console.Out.WriteLine("=== 终端颜色测试 ===\n");
            Console.Out.WriteLine("基础颜色:");
            Console.Out.WriteLine("  \x1b[32m绿色(成功)\x1b[0m  \x1b[31m红色(错误)\x1b[0m  \x1b[33m黄色(警告)\x1b[0m");
            Console.Out.WriteLine("  \x1b[90m灰色(次要)\x1b[0m  \x1b[34m蓝色(主要)\x1b[0m  \x1b[36m青色(强调)\x1b[0m");
            Console.Out.WriteLine();
            Console.Out.WriteLine("ANSI 256色:");
            for (var i = 0; i < 16; i++) Console.Out.Write($"\x1b[38;5;{i}m{i:D2}\x1b[0m ");
            Console.Out.WriteLine();
            Console.Out.WriteLine("样式:");
            Console.Out.WriteLine("  \x1b[1m粗体\x1b[0m  \x1b[3m斜体\x1b[0m  \x1b[4m下划线\x1b[0m");
        } else if (action is "reset" or "default") {
            Console.Out.WriteLine("颜色已重置为默认");
        } else {
            Console.Out.WriteLine($"颜色主题: {action}");
            Console.Out.WriteLine("使用 /color test 测试终端颜色支持");
            Console.Out.WriteLine("使用 /color reset 重置为默认");
        }
        return Task.FromResult(0);
    }

    private static Task<int> EnvAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        var filter = o.TryGetValue("filter", out var f) ? f : "";
        var vars = Environment.GetEnvironmentVariables()
            .Cast<System.Collections.DictionaryEntry>()
            .Select(e => (k: e.Key?.ToString() ?? "", v: e.Value?.ToString() ?? ""))
            .Where(x => filter.Length == 0 || x.k.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.k, StringComparer.Ordinal);
        foreach (var (k, v) in vars) Console.Out.WriteLine($"{k}={v}");
        Console.Out.WriteLine("(读取的是独立 exe 进程环境变量；宿主会话级变量不在此处)");
        return Task.FromResult(0);
    }

    private static Task<int> HelpAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        Console.Out.WriteLine("jcc-tool-slash — 斜杠命令节点");
        Console.Out.WriteLine();
        Console.Out.WriteLine("用法: jcc tool call slash <命令> [参数...]");
        Console.Out.WriteLine();
        Console.Out.WriteLine("本地实现: version, color, env, help, heapdump");
        Console.Out.WriteLine("其余命令依赖宿主会话上下文，由 jcc slash_call 执行。");
        Console.Out.WriteLine("用 jcc tool tree 查看全部 99 个斜杠命令。");
        return Task.FromResult(0);
    }

    private static Task<int> HeapdumpAsync(IReadOnlyDictionary<string, string> o, CancellationToken ct) {
        Console.Out.WriteLine("堆转储 / 运行时诊断（独立 exe 进程）:");
        Console.Out.WriteLine();
        var memory = GC.GetTotalMemory(false);
        var compacted = GC.GetTotalMemory(true);
        Console.Out.WriteLine($"  当前托管内存: {memory / 1024 / 1024:F1} MB");
        Console.Out.WriteLine($"  压缩后内存:   {compacted / 1024 / 1024:F1} MB");
        Console.Out.WriteLine($"  GC 最大代数:  {GC.MaxGeneration}");
        Console.Out.WriteLine();
        Console.Out.WriteLine("  GC 集合次数:");
        for (var i = 0; i <= GC.MaxGeneration; i++) Console.Out.WriteLine($"    {i} 代: {GC.CollectionCount(i)}");
        Console.Out.WriteLine();
        ThreadPool.GetAvailableThreads(out var w, out var io);
        ThreadPool.GetMaxThreads(out var mw, out var mio);
        Console.Out.WriteLine($"  线程池: 工作 {w}/{mw}  I/O {io}/{mio}");
        var p = Process.GetCurrentProcess();
        Console.Out.WriteLine($"  进程内存: {p.WorkingSet64 / 1024 / 1024:F1} MB");
        Console.Out.WriteLine($"  句柄数:   {p.HandleCount}");
        Console.Out.WriteLine("(注意: 诊断的是本独立 exe 进程，非宿主 jcc 进程)");
        return Task.FromResult(0);
    }
}
