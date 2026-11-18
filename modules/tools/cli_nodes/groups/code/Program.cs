// HANDCRAFTED — DO NOT OVERWRITE BY GENERATOR


namespace JoinCode.CliNode.Code;

/// <summary>
/// code 分组独立 CLI.exe — 第 8 个「逐组做实」样板。
/// <para>本地实现 32 个（全组）：10 个 notebook_*（.ipynb 纯 JSON）、9 个 C# LLM 分析/生成（HttpClient 直调）、
/// 3 个 recall_*（读 ~/.jcc/notebook 文件）、10 个 lsp_*（exe 自己起语言服务器，JSON-RPC over stdio，见 LspConnection）。</para>
/// -h 文本来自现有 [McpTool] 特性；入口逻辑统一在 CliNodeRunner。
/// </summary>
internal static class Program {
    private static Task<int> Main(string[] args) => CliNodeRunner.RunAsync(new CliNodeDefinition {
        GroupId = "code",
        Title = "code 分组",
        HelpResource = "JoinCode.CliNodes.Groups.code.help.txt",
        Resolve = CodeCommands.Find,
        LocalToolIds = () => CodeCommands.LocalToolIds,
        HostReason = "code 组全部本地实现，正常不会走到转发。"
    }, args);
}
