// HANDCRAFTED — DO NOT OVERWRITE BY GENERATOR
using JoinCode.Abstractions.Tools;

namespace JoinCode.CliNode.Code;

/// <summary>
/// code 分组独立 CLI.exe — 第 8 个「逐组做实」样板。
/// <para>本地实现 10 个 notebook_* 工具：.ipynb 是纯 JSON，BCL 直读直写即可。</para>
/// -h 文本来自现有 [McpTool] 特性；入口逻辑统一在 CliNodeRunner。
/// </summary>
internal static class Program {
    private static Task<int> Main(string[] args) => CliNodeRunner.RunAsync(new CliNodeDefinition {
        GroupId = "code",
        Title = "code 分组",
        HelpResource = "JoinCode.CliNodes.Groups.code.help.txt",
        Resolve = NotebookCommands.Find,
        LocalToolIds = () => NotebookCommands.LocalToolIds,
        HostReason = "lsp_* 需语言服务器进程、generate_*/analyze_* 需模型调用、recall_* 需宿主召回库，"
            + "这些都不是零依赖 exe 内能重建的能力。"
    }, args);
}
