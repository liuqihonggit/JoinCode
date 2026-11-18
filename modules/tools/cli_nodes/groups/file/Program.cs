// HANDCRAFTED — DO NOT OVERWRITE BY GENERATOR


namespace JoinCode.CliNode.File;

/// <summary>
/// file 分组独立 CLI.exe — 第 2 个「逐组做实」样板（第 1 个是 git 组）。
/// <para>11 个文件工具用 BCL 本地执行；无法零依赖实现的工具转发宿主 jcc mcp_call。</para>
/// -h 文本来自现有 [McpTool] 特性；入口逻辑统一在 CliNodeRunner，本文件只声明身份。
/// </summary>
internal static class Program {
    private static Task<int> Main(string[] args) => CliNodeRunner.RunAsync(new CliNodeDefinition {
        GroupId = "file",
        Title = "file 分组",
        HelpResource = "JoinCode.CliNodes.Groups.file.help.txt",
        Resolve = FileCommands.Find,
        LocalToolIds = () => FileCommands.LocalToolIds,
        HostReason = "file_apply_patch 需要宿主的多文件补丁事务与冲突校验，send_user_file 需要宿主附件管道。"
    }, args);
}
