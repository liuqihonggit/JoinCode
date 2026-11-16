// HANDCRAFTED — DO NOT OVERWRITE BY GENERATOR
using JoinCode.Abstractions.Tools;

namespace JoinCode.CliNode.Mcp;

/// <summary>
/// mcp 分组独立 CLI.exe — 7 个认证配置工具本地实现（读写 ~/.jcc/mcp/auth.json）。
/// mcp_connect/disconnect/list_tools/call_tool 等 18 个依赖宿主进程内活跃连接对象（stdio 子进程管道、
/// SSE/WebSocket 长连接）+ ToolRegistry 注册，随进程退出销毁，物理上无法在短生命周期 exe 等价，留宿主。
/// -h 文本来自 [McpTool] 特性；入口逻辑统一在 CliNodeRunner。
/// </summary>
internal static class Program {
    private static Task<int> Main(string[] args) => CliNodeRunner.RunAsync(new CliNodeDefinition {
        GroupId = "mcp",
        Title = "mcp 分组",
        HelpResource = "JoinCode.CliNodes.Groups.mcp.help.txt",
        Resolve = McpAuthCommands.Find,
        LocalToolIds = () => McpAuthCommands.LocalToolIds,
        HostReason = "mcp 连接/工具调用/资源读取类依赖宿主进程内活跃连接对象（stdio 管道/长连接）与工具注册表，"
            + "进程退出即失效，短生命周期 exe 无法等价，由宿主执行。"
    }, args);
}
