// HANDCRAFTED — DO NOT OVERWRITE BY GENERATOR
using JoinCode.Abstractions.Tools;

namespace JoinCode.CliNode.Search;

/// <summary>
/// search 分组独立 CLI.exe — 第 7 个「逐组做实」样板。
/// <para>本地实现 8 个: glob / search_files 用内置 glob 匹配器；grep / search_text / search_code /
/// code_search / search_codebase / symbol_search 优先调用 ripgrep，缺失时退回内置扫描。</para>
/// -h 文本来自现有 [McpTool] 特性；入口逻辑统一在 CliNodeRunner。
/// </summary>
internal static class Program {
    private static Task<int> Main(string[] args) => CliNodeRunner.RunAsync(new CliNodeDefinition {
        GroupId = "search",
        Title = "search 分组",
        HelpResource = "JoinCode.CliNodes.Groups.search.help.txt",
        Resolve = SearchCommands.Find,
        LocalToolIds = () => SearchCommands.LocalToolIds,
        HostReason = "code_index_* 需持久化 AST 索引库、graph_* 需调用图存储、semantic/document 需向量嵌入模型，"
            + "这些都是宿主持有的状态与模型，不是本 exe 内能重建的。"
    }, args);
}
