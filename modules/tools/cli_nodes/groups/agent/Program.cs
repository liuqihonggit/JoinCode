// HANDCRAFTED — DO NOT OVERWRITE BY GENERATOR
using JoinCode.Abstractions.Tools;

namespace JoinCode.CliNode.Agent;

/// <summary>
/// agent 分组独立 CLI.exe。
/// <para>入口行为（-h / --list-local / 参数解析 / 本地分发 / 转发 / 退出码）统一在 <see cref="CliNodeRunner"/>。</para>
/// <para>本地实现只有 worktree 的两个纯 git 查询（见 private/AgentCommands.cs）；其余 57 个工具
/// 依赖宿主的计划状态、子代理调度、任务队列与团队消息，exe 内重建会分叉出第二套真相源。</para>
/// </summary>
internal static class Program {
    private static Task<int> Main(string[] args) => CliNodeRunner.RunAsync(new CliNodeDefinition {
        GroupId = "agent",
        Title = "agent 分组",
        HelpResource = "JoinCode.CliNodes.Groups.agent.help.txt",
        Resolve = AgentCommands.Find,
        LocalToolIds = static () => AgentCommands.LocalToolIds,
        HostReason = "计划模式、子代理、任务队列与团队消息的状态都在宿主进程内，独立 exe 无法读取。"
    }, args);
}
