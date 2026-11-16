// HANDCRAFTED — DO NOT OVERWRITE BY GENERATOR
using JoinCode.Abstractions.Tools;

namespace JoinCode.CliNode.Shell;

/// <summary>
/// shell 分组独立 CLI.exe — 第 4 个「逐组做实」样板。
/// <para>本地实现 11 个（命令执行、错误诊断、git 辅助等），依赖引擎运行态的 16 个工具转发宿主。</para>
/// <para><b>安全边界</b>：本 exe 内的 bash / powershell 直连操作系统，绕过宿主的 Shell 中间件栈
/// 与 DangerousCommandCatalog 校验。生产环境的命令执行应仍走宿主；本组本地实现只用于
/// 进程隔离验证与离线可用性。</para>
/// -h 文本来自现有 [McpTool] 特性；入口逻辑统一在 CliNodeRunner。
/// </summary>
internal static class Program {
    private static Task<int> Main(string[] args) => CliNodeRunner.RunAsync(new CliNodeDefinition {
        GroupId = "shell",
        Title = "shell 分组",
        HelpResource = "JoinCode.CliNodes.Groups.shell.help.txt",
        Resolve = ShellCommands.Find,
        LocalToolIds = () => ShellCommands.LocalToolIds,
        HostReason = "构建/沙箱/终端会话类工具依赖宿主进程树、中间件栈与命令安全校验，不能脱离宿主执行。"
    }, args);
}
