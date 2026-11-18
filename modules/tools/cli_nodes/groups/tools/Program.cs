// HANDCRAFTED — DO NOT OVERWRITE BY GENERATOR


namespace JoinCode.CliNode.Tools;

/// <summary>
/// tools 分组独立 CLI.exe — 第 3 个「逐组做实」混合模式样板（前两个是 git / file 组）。
/// <para>本地命令表命中的工具用 BCL 本地执行；未本地化的工具自动转发宿主 jcc mcp_call。</para>
/// -h 文本来自内嵌资源 JoinCode.CliNodes.Groups.tools.help.txt；入口逻辑统一在 CliNodeRunner。
/// </summary>
internal static class Program {
    private static Task<int> Main(string[] args) => CliNodeRunner.RunAsync(new CliNodeDefinition {
        GroupId = "tools",
        Title = "tools 分组",
        HelpResource = "JoinCode.CliNodes.Groups.tools.help.txt",
        Resolve = ToolsCommands.Find,
        LocalToolIds = () => ToolsCommands.LocalToolIds,
        HostReason = "skill_* / tool_* 依赖宿主技能注册表与工具索引，零依赖 exe 无法读取这些进程内状态。"
    }, args);
}
