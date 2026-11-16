// HANDCRAFTED — DO NOT OVERWRITE BY GENERATOR
using JoinCode.Abstractions.Tools;

namespace JoinCode.CliNode.Code;

/// <summary>
/// code 分组独立 CLI.exe — 第 8 个「逐组做实」样板。
/// <para>本地实现 22 个：10 个 notebook_*（.ipynb 纯 JSON）、9 个 C# LLM 分析/生成（HttpClient 直调）、
/// 3 个 recall_*（读 ~/.jcc/notebook 文件）。10 个 lsp_* 绑定宿主常驻语言服务器与跨调用语义索引，保留 host。</para>
/// -h 文本来自现有 [McpTool] 特性；入口逻辑统一在 CliNodeRunner。
/// </summary>
internal static class Program {
    private static Task<int> Main(string[] args) => CliNodeRunner.RunAsync(new CliNodeDefinition {
        GroupId = "code",
        Title = "code 分组",
        HelpResource = "JoinCode.CliNodes.Groups.code.help.txt",
        Resolve = CodeCommands.Find,
        LocalToolIds = () => CodeCommands.LocalToolIds,
        HostReason = "lsp_* 需宿主常驻的语言服务器进程与跨调用语义索引（didOpen/didChange），"
            + "短生命周期 exe 冷启动无法等价，保留宿主执行。"
    }, args);
}
