// HANDCRAFTED — DO NOT OVERWRITE BY GENERATOR
using JoinCode.Abstractions.Tools;

namespace JoinCode.CliNode.Web;

/// <summary>
/// web 分组独立 CLI.exe — 第 6 个「逐组做实」样板。
/// <para>本地实现 3 个: download_file（分块并发 + 断点续传 + MD5）、web_to_markdown（HTML→Markdown）、
/// web_fetch（抓取 + 二级模型加工，跨主机跳转不自动跟随）。</para>
/// -h 文本来自现有 [McpTool] 特性，本文件不重复维护任何说明；入口逻辑统一在 CliNodeRunner。
/// </summary>
internal static class Program {
    private static Task<int> Main(string[] args) => CliNodeRunner.RunAsync(new CliNodeDefinition {
        GroupId = "web",
        Title = "web 分组",
        HelpResource = "JoinCode.CliNodes.Groups.web.help.txt",
        Resolve = WebCommands.Find,
        LocalToolIds = () => WebCommands.LocalToolIds,
        HostReason = "web_search 是 7 家 provider 的选择矩阵（同一决策不该有两个实现）、web_browser 需宿主浏览器会话。"
    }, args);
}
