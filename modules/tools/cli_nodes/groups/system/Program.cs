// HANDCRAFTED — DO NOT OVERWRITE BY GENERATOR
using JoinCode.Abstractions.Tools;

namespace JoinCode.CliNode.System;

/// <summary>
/// system 分组独立 CLI.exe — 第 9 个「逐组做实」样板。
/// <para>本地实现 3 个无状态工具: sleep / sleep_until / get_process_info。</para>
/// -h 文本来自现有 [McpTool] 特性；入口逻辑统一在 CliNodeRunner。
/// </summary>
internal static class Program {
    private static Task<int> Main(string[] args) => CliNodeRunner.RunAsync(new CliNodeDefinition {
        GroupId = "system",
        Title = "system 分组",
        HelpResource = "JoinCode.CliNodes.Groups.system.help.txt",
        Resolve = SystemCommands.Find,
        LocalToolIds = () => SystemCommands.LocalToolIds,
        HostReason = "config_*/permission_*/agent_*/memory_*/analytics_* 读写宿主配置、权限规则、"
            + "智能体注册表与记忆库；本 exe 复制一份只会分叉出第二个真相源，因此仍由宿主执行。"
    }, args);
}
