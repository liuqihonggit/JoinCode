// HANDCRAFTED — DO NOT OVERWRITE BY GENERATOR


namespace JoinCode.CliNode.Git;

/// <summary>
/// git 分组独立 CLI.exe 入口 — ADR 0157 v2「按分组一个 exe」的第一个真实样板。
/// <para>10 个 git 工具全部本地实现：只把参数翻译成 git 命令行，再交给 <see cref="GitRunner"/> 执行，
/// 不引用引擎、不初始化 DI。</para>
/// -h 文本由 migrate_mcp_tools_to_cli_nodes.py 从现有 [McpTool] 特性编译期提取，
/// 本程序不重复维护任何工具说明；入口逻辑统一在 CliNodeRunner。
/// </summary>
internal static class Program {
    private static Task<int> Main(string[] args) => CliNodeRunner.RunAsync(new CliNodeDefinition {
        GroupId = "git",
        Title = "git 分组",
        HelpResource = "JoinCode.CliNodes.Groups.git.help.txt",
        Resolve = Resolve,
        LocalToolIds = () => GitCommandCatalog.ToolIds,
        HostReason = "git 组全部本地实现，正常不会走到转发。"
    }, args);

    /// <summary>把工具 id 与参数表翻译成 git 命令行；返回 null 表示该 id 不在本组。</summary>
    private static CliNodeHandler? Resolve(string toolId) {
        if (!GitCommandCatalog.TryGet(toolId, out var builder)) return null;
        return (options, ct) => RunAsync(toolId, builder, options, ct);
    }

    private static Task<int> RunAsync(
        string toolId, Func<ArgBag, string> builder,
        IReadOnlyDictionary<string, string> options, CancellationToken ct) {
        var bag = new ArgBag(options);
        var gitArgs = builder(bag);
        var workDir = toolId == "git_clone" ? bag.Get("parent_dir") : bag.WorkingDirectory;
        return GitRunner.RunAsync(gitArgs, workDir, ct);
    }
}
