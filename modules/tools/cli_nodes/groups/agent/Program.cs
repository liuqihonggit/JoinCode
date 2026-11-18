// HANDCRAFTED — DO NOT OVERWRITE BY GENERATOR


namespace JoinCode.CliNode.Agent;

/// <summary>
/// agent 分组独立 CLI.exe。
/// <para>入口行为（-h / --list-local / 参数解析 / 本地分发 / 转发 / 退出码）统一在 <see cref="CliNodeRunner"/>。</para>
/// <para>本地实现 29 个：task/todo/plan 状态机（文件化在 ~/.jcc）、cron 表达式校验、complete_step、
/// 以及 worktree 全生命周期 8 个（走 git 原生命令，git 自己就是真相源）。</para>
/// <para>其余 30 个（subagent_*/team_*/plan 模式切换/goal 推进/cron 写/任务队列查询）依赖宿主进程内的
/// 调度器与活 agent 状态，exe 内重建会分叉出第二套真相源。</para>
/// </summary>
internal static class Program {
    private static Task<int> Main(string[] args) => CliNodeRunner.RunAsync(new CliNodeDefinition {
        GroupId = "agent",
        Title = "agent 分组",
        HelpResource = "JoinCode.CliNodes.Groups.agent.help.txt",
        Resolve = AgentCommands.Find,
        LocalToolIds = static () => AgentCommands.LocalToolIds,
        HostReason = "计划模式切换、子代理调度、团队消息与 cron 调度器的状态都在宿主进程内，独立 exe 无法读取。"
    }, args);
}
